namespace FSharp.Data

open System.Reflection
open System.Collections.Generic
open System.Data
open System.Data.Common
open System
open System.IO
open System.Collections.Concurrent

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
    let cache = ConcurrentDictionary()

    do 
        tempAssembly.AddTypes  <| [ providerType ]
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
                ProvidedStaticParameter("Provider", typeof<string>, "System.Data.SqlClient") 
                //ProvidedStaticParameter("CLITypes", typeof<bool>, false) 
            ],             
            instantiationFunction = (fun typeName args ->   
                let key = args |> Array.map string |> String.concat ""
                match cache.TryGetValue( args) with
                | false, _ ->
                    let v = this.CreateType typeName args
                    cache.[args] <- v
                    v
                | true, v -> v
            )        
        )

        providerType.AddXmlDoc """
<summary>Enumeration based on SQL query.</summary> 
<param name='Query'>SQL used to get the enumeration labels and values. A result set must have at least two columns. The first one is a label.</param>
<param name='ConnectionString'>String used to open a data connection.</param>
<param name='Provider'>Invariant name of a ADO.NET provider. Default is "System.Data.SqlClient".</param>
"""

        this.AddNamespace( nameSpace, [ providerType ])
    
    member internal this.CreateType typeName parameters = 
        let query : string = unbox parameters.[0] 
        let connectionString : string = unbox parameters.[1] 
        let adoProviderName : string = unbox parameters.[2] 
        //let cliTypes : bool = unbox unbox parameters.[3] 

        let providedEnumType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes <| [ providedEnumType ]
        
        let adoObjectsFactory = DbProviderFactories.GetFactory( adoProviderName)

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
    
        let tryParse = ProvidedMethod( "TryParse", [ ProvidedParameter("value", typeof<string>) ], typedefof<_ option>.MakeGenericType( valueType), IsStaticMethod = true)
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
                %%names
                |> Array.tryFindIndex (fun (x: string) -> x = %%args.[0]) 
                |> Option.map (fun index -> Array.get<'Value> %%values index)
            @@>



