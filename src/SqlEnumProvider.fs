namespace FSharp.Data

open System.Reflection
open System.Collections.Generic
open System.Data
open System.Data.Common
open System
open System.IO
open System.Collections.Concurrent
open System.Configuration

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Reflection

open Samples.FSharp.ProvidedTypes

[<assembly:TypeProviderAssembly()>]
do()

[<TypeProvider>]
type public SqlEnumProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition( assembly, nameSpace, "SqlEnumProvider", Some typeof<obj>, HideObjectMethods = true, IsErased = false)
    
    let tempAssembly = ProvidedAssembly( Path.ChangeExtension(Path.GetTempFileName(), ".dll"))
    let cache = ConcurrentDictionary<_, ProvidedTypeDefinition>()

    do 
        tempAssembly.AddTypes  <| [ providerType ]
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("Provider", typeof<string>, "System.Data.SqlClient") 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                //ProvidedStaticParameter("CLIEnum", typeof<bool>, false) 
            ],             
            instantiationFunction = (fun typeName args ->   
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3]
                cache.GetOrAdd( key, this.CreateRootType)
            )        
        )

        providerType.AddXmlDoc """
<summary>Enumeration based on SQL query.</summary> 
<param name='Query'>SQL used to get the enumeration labels and values. A result set must have at least two columns. The first one is a label.</param>
<param name='ConnectionString'>String used to open a data connection.</param>
<param name='Provider'>Invariant name of a ADO.NET provider. Default is "System.Data.SqlClient".</param>
"""

        this.AddNamespace( nameSpace, [ providerType ])
    
    member internal this.CreateRootType( typeName, query: string, connectionStringOrName: string, adoProviderName: string, configFile) = 

        let providedEnumType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes <| [ providedEnumType ]
        
        let adoObjectsFactory = DbProviderFactories.GetFactory( adoProviderName)

        let connectionString = SqlEnumProvider.ParseConnectionStringName( connectionStringOrName, config.ResolutionFolder, configFile)

        use conn = adoObjectsFactory.CreateConnection() 
        conn.ConnectionString <- connectionString
        conn.Open()

        use cmd = adoObjectsFactory.CreateCommand() 
        cmd.CommandText <- query
        cmd.Connection <- conn
        cmd.CommandType <- CommandType.Text

        use reader = cmd.ExecuteReader()
        if reader.FieldCount < 2 then failwith "At least two columns expected in result rowset. Received %i columns." reader.FieldCount
        let schema = reader.GetSchemaTable()
        let valueType, getValue = 
            
            let getValueType(row: DataRow) = 
                let t = Type.GetType( typeName = string row.["DataType"])
                if not( t.IsValueType || t = typeof<string>)
                then 
                    failwithf "Invalid type %s of column %O for value part. Only .NET value types and strings supported as value." t.FullName row.["ColumnName"]
                t

            if schema.Rows.Count = 2
            then
                let valueType = getValueType schema.Rows.[1]
                let getValue = fun(values: obj[]) -> Expr.Value(Array.get values 0, valueType)
                valueType, getValue
            else
                let tupleItemTypes = 
                    schema.Rows
                    |> Seq.cast<DataRow>
                    |> Seq.skip 1
                    |> Seq.map getValueType
                
                let tupleType = tupleItemTypes |> Seq.toArray |> FSharpType.MakeTupleType
                let getValue = fun values -> (values, tupleItemTypes) ||> Seq.zip |> Seq.map Expr.Value |> Seq.toList |> Expr.NewTuple
                                
                tupleType, getValue

        let names, values = 
            [ 
                while reader.Read() do 
                    let rowValues = Array.zeroCreate reader.FieldCount
                    let count = reader.GetValues( rowValues)
                    assert (count = rowValues.Length)
                    let label = string rowValues.[0]
                    let tailAsValue = Array.sub rowValues 1 (count - 1) |> getValue
                    yield label, tailAsValue
            ] 
            |> List.unzip

        names 
        |> Seq.groupBy id 
        |> Seq.iter (fun (key, xs) -> if Seq.length xs > 1 then failwithf "Non-unique label %s." key)

        let namesStorage = ProvidedField( "Names", typeof<string[]>)
        namesStorage.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
        providedEnumType.AddMember namesStorage

        let valuesStorage = ProvidedField( "Values", valueType.MakeArrayType())
        valuesStorage.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
        providedEnumType.AddMember valuesStorage 

        let typeInit = ProvidedConstructor([], IsTypeInitializer = true)
        typeInit.InvokeCode <- fun _ -> 
            Expr.Sequential(
                Expr.FieldSet(namesStorage, Expr.NewArray( typeof<string>, names |> List.map Expr.Value)),
                Expr.FieldSet(valuesStorage, Expr.NewArray( valueType, values))
            )

        providedEnumType.AddMember typeInit 

        (names, values) ||> List.iter2 (fun name value -> 
            let property = ProvidedProperty( name, valueType, IsStatic = true, GetterCode = fun _ -> value)
            providedEnumType.AddMember( property)
        )
    
        let tryParse = 
            ProvidedMethod(
                methodName = "TryParse", 
                parameters = [ 
                    ProvidedParameter("value", typeof<string>) 
                    ProvidedParameter("ignoreCase", typeof<bool>, optionalValue = false) 
                ], 
                returnType = typedefof<_ option>.MakeGenericType( valueType), 
                IsStaticMethod = true
            )

        tryParse.InvokeCode <- 
            let m = this.GetType().GetMethod( "GetTryParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
            this.GetType()
                .GetMethod( "GetTryParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                .MakeGenericMethod( valueType)
                .Invoke( null, [| Expr.FieldGet( namesStorage); Expr.FieldGet( valuesStorage) |])
                |> unbox

        providedEnumType.AddMember tryParse

        providedEnumType

    static member internal GetTryParseImpl<'Value>( names, values) = 
        fun (args: _ list) ->
            <@@
                let comparer = 
                    if %%args.[1]
                    then StringComparer.InvariantCultureIgnoreCase
                    else StringComparer.InvariantCulture

                %%names
                |> Array.tryFindIndex (fun (x: string) -> comparer.Equals(x, %%args.[0])) 
                |> Option.map (fun index -> Array.get<'Value> %%values index)
            @@>

    static member internal ParseConnectionStringName(s: string, resolutionFolder, configFile) =
        match s.Trim().Split([|'='|], 2, StringSplitOptions.RemoveEmptyEntries) with
            | [| "" |] -> invalidArg "ConnectionStringOrName" "Value is empty!"
            | [| prefix; tail |] when prefix.Trim().ToLower() = "name" -> 
                SqlEnumProvider.ReadConnectionStringFromConfigFileByName( tail.Trim(), resolutionFolder, configFile)
            | _ -> s

    static member internal ReadConnectionStringFromConfigFileByName(name: string, resolutionFolder, fileName) =

        let configFilename = 
            if fileName <> "" 
            then
                let path = Path.Combine(resolutionFolder, fileName)
                if not <| File.Exists path 
                then raise <| FileNotFoundException( sprintf "Could not find config file '%s'." path)
                else path
            else
                let appConfig = Path.Combine(resolutionFolder, "app.config")
                let webConfig = Path.Combine(resolutionFolder, "web.config")

                if File.Exists appConfig then appConfig
                elif File.Exists webConfig then webConfig
                else failwithf "Cannot find either app.config or web.config."
        
        let map = ExeConfigurationFileMap()
        map.ExeConfigFilename <- configFilename
        let configSection = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None).ConnectionStrings.ConnectionStrings
        match configSection, lazy configSection.[name] with
        | null, _ | _, Lazy null -> failwithf "Cannot find name %s in <connectionStrings> section of %s file." name configFilename
        | _, Lazy x -> x.ConnectionString
