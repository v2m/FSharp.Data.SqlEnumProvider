﻿namespace FSharp.Data

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

type ApiStyle = | Default = 0 | ``C#`` = 1 | Enum = 2 

[<TypeProvider>]
type public SqlEnumProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly() 
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlEnumProvider", Some typeof<obj>, HideObjectMethods = true, IsErased = false)
    let tempAssembly = ProvidedAssembly( Path.ChangeExtension(Path.GetTempFileName(), ".dll"))
    do tempAssembly.AddTypes [providerType]
    let cache = ConcurrentDictionary<_, ProvidedTypeDefinition>()

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionStringOrName", typeof<string>) 
                ProvidedStaticParameter("Provider", typeof<string>, "System.Data.SqlClient") 
                ProvidedStaticParameter("ConfigFile", typeof<string>, "") 
                ProvidedStaticParameter("ApiStyle", typeof<ApiStyle>, ApiStyle.Default) 
            ],             
            instantiationFunction = (fun typeName args ->   
                let key = typeName, unbox args.[0], unbox args.[1], unbox args.[2], unbox args.[3], unbox args.[4]
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
    
    member internal this.CreateRootType( typeName, query: string, connectionStringOrName: string, adoProviderName: string, configFile, apiStyle) = 
        let tempAssembly = ProvidedAssembly( Path.ChangeExtension(Path.GetTempFileName(), ".dll"))

        let providedEnumType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true, IsErased = false)
        tempAssembly.AddTypes [providedEnumType]
        //providerType.AddMember providedEnumType
//        tempAssembly.AddTypes <| [ providedEnumType ]
        
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
        if not reader.HasRows then failwith "Resultset is empty. At least one row expected." 
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
                let getValue (values : obj[]) = values.[0] 

                valueType, getValue
            else
                let tupleItemTypes = 
                    schema.Rows
                    |> Seq.cast<DataRow>
                    |> Seq.skip 1
                    |> Seq.map getValueType
                
                let tupleType = tupleItemTypes |> Seq.toArray |> FSharpType.MakeTupleType
                let getValue (values : obj[]) = FSharpValue.MakeTuple(values, tupleType)
                
                tupleType, getValue

        let names, values = 
            [ 
                while reader.Read() do 
                    let rowValues = Array.zeroCreate reader.FieldCount
                    let count = reader.GetValues( rowValues)
                    assert (count = rowValues.Length)
                    let label = string rowValues.[0]
                    let value = Array.sub rowValues 1 (count - 1) |> getValue
                    yield label, value
            ] 
            |> List.unzip

        names 
        |> Seq.groupBy id 
        |> Seq.iter (fun (key, xs) -> if Seq.length xs > 1 then failwithf "Non-unique label %s." key)

        if apiStyle = ApiStyle.Enum
        then 
            let allowedTypesForEnum = 
                [| typeof<sbyte>; typeof<byte>; typeof<int16>; typeof<uint16>; typeof<int32>; typeof<uint32>; typeof<int64>; typeof<uint16>; typeof<uint64>; typeof<char> |]
            
            if not(allowedTypesForEnum |> Array.exists valueType.Equals)
            then failwithf "Enumerated types can only have one of the following underlying types: %A." [| for t in allowedTypesForEnum -> t.Name |]

            providedEnumType.SetBaseType typeof<Enum>

            (names, values)
            ||> List.map2 (fun name value -> ProvidedLiteralField(name, valueType, value))
            |> providedEnumType.AddMembers

        else
            let valueFields, setValues = 
                (names, values) ||> List.map2 (fun name value -> 
                    let field = ProvidedField( name, valueType)
                    field.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
                    field, Expr.FieldSet(field, Expr.Value(value, valueType))
                ) 
                |> List.unzip

            valueFields |> List.iter providedEnumType.AddMember

            let namesStorage = ProvidedField( "Names", typeof<string[]>)
            namesStorage.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
            providedEnumType.AddMember namesStorage

            let valuesStorage = ProvidedField( "Values", valueType.MakeArrayType())
            valuesStorage.SetFieldAttributes( FieldAttributes.Public ||| FieldAttributes.InitOnly ||| FieldAttributes.Static)
            providedEnumType.AddMember valuesStorage 

            let namesExpr = Expr.NewArray(typeof<string>, names |> List.map Expr.Value)
            let valuesExpr = Expr.NewArray(valueType, [ for x in values -> Expr.Value(x, valueType) ])

            let typeInit = ProvidedConstructor([], IsTypeInitializer = true)
            typeInit.InvokeCode <- fun _ -> 
                Expr.Sequential(
                    Expr.Sequential(
                        Expr.FieldSet(namesStorage, Expr.NewArray( typeof<string>, names |> List.map Expr.Value)),
                        Expr.FieldSet(valuesStorage, Expr.NewArray( valueType, [ for x in values -> Expr.Value(x, valueType) ]))
                    ),
                    setValues |> List.reduce (fun x y -> Expr.Sequential(x, y))
                )

            providedEnumType.AddMember typeInit 

            if apiStyle = ApiStyle.Default
            then 
                let tryParse2Arg = 
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>) // optional=false 
                        ], 
                        returnType = typedefof<_ option>.MakeGenericType( valueType), 
                        IsStaticMethod = true
                    )

                tryParse2Arg.InvokeCode <- 
                    this.GetType()
                        .GetMethod( "GetTryParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| namesExpr; valuesExpr |])
                        |> unbox

                providedEnumType.AddMember tryParse2Arg

                let tryParse1Arg = 
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                        ], 
                        returnType = typedefof<_ option>.MakeGenericType( valueType), 
                        IsStaticMethod = true
                    )

                tryParse1Arg.InvokeCode <- fun [arg] -> Expr.Call(tryParse2Arg, [arg; Expr.Value false])

                providedEnumType.AddMember tryParse1Arg


            elif apiStyle = ApiStyle.``C#``
            then 
                let invokeCode = 
                    this.GetType()
                        .GetMethod("GetTryParseImplForCSharp", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| namesExpr; valuesExpr |])
                        |> unbox
                [
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("result", valueType.MakeByRefType(), isOut = true) 
                        ], 
                        returnType = typeof<bool>, 
                        IsStaticMethod = true,
                        InvokeCode = invokeCode
                    )

                    //ignoreCase param
                    ProvidedMethod(
                        methodName = "TryParse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>) 
                            ProvidedParameter("result", valueType.MakeByRefType(), isOut = true) 
                        ], 
                        returnType = typeof<bool>, 
                        IsStaticMethod = true,
                        InvokeCode = invokeCode
                    )
                ] |> providedEnumType.AddMembers

            do 
                let parseImpl =
                    this.GetType()
                        .GetMethod( "GetParseImpl", BindingFlags.NonPublic ||| BindingFlags.Static)
                        .MakeGenericMethod( valueType)
                        .Invoke( null, [| namesExpr; valuesExpr; providedEnumType.FullName |])
                        |> unbox

                let parse2Arg = 
                    ProvidedMethod(
                        methodName = "Parse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                            ProvidedParameter("ignoreCase", typeof<bool>) 
                        ], 
                        returnType = valueType, 
                        IsStaticMethod = true, 
                        InvokeCode = parseImpl
                    )

                providedEnumType.AddMember parse2Arg

                let parse1Arg = 
                    ProvidedMethod(
                        methodName = "Parse", 
                        parameters = [ 
                            ProvidedParameter("value", typeof<string>) 
                        ], 
                        returnType = valueType, 
                        IsStaticMethod = true, 
                        InvokeCode = fun [arg] -> Expr.Call(parse2Arg, [arg; Expr.Value false])
                    )

                providedEnumType.AddMember parse1Arg

        providedEnumType

    //Quotation factories
    
    static member internal GetTryParseImpl<'Value>( names, values) = 
        fun (args: _ list) ->
            <@@
                if String.IsNullOrEmpty (%%args.[0]) then nullArg "value"

                let comparer = 
                    if %%args.[1]
                    then StringComparer.InvariantCultureIgnoreCase
                    else StringComparer.InvariantCulture

                %%names
                |> Array.tryFindIndex (fun (x: string) -> comparer.Equals(x, %%args.[0])) 
                |> Option.map (fun index -> Array.get<'Value> %%values index)
            @@>

    static member OptionToRef<'T>(input, x: 'T byref) = match input with | Some value -> x <- value; true | None -> false

    static member internal GetTryParseImplForCSharp<'Value>( names, values) = 
        fun args -> 
            let value, ignoreCase, result = 
                match args with
                | [ x; y; z ] -> x, y, z
                | [ x; y ] -> x, Expr.Value false, y
                | _ -> failwith "Unexpected"
            let expr = (SqlEnumProvider.GetTryParseImpl<'Value> (names, values)) [ value; ignoreCase ]
            let optionToRef = typeof<SqlEnumProvider>.GetMethod("OptionToRef").MakeGenericMethod(typeof<'Value>)
            Expr.Call(optionToRef, [ expr; result])

    static member internal GetParseImpl<'Value>( names, values, typeName) = 
        fun (args: _ list) ->
            let expr = (SqlEnumProvider.GetTryParseImpl<'Value> (names, values)) args
            <@@
                match %%expr with
                | Some(x : 'Value) -> x
                | None -> 
                    let errMsg = sprintf @"Cannot convert value ""%s"" to type ""%s""" %%args.[0] typeName
                    invalidArg "value" errMsg
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
