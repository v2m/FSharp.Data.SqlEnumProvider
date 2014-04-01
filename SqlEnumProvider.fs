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
type public SqlCommandProvider(config : TypeProviderConfig) as this = 
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
            ],             
            instantiationFunction = (fun typeName args ->   
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
            if schema.Rows.Count = 2
            then
                let valueType = Type.GetType( typeName = string schema.Rows.[1].["DataType"])
                let getValue = fun(values: obj[]) -> Expr.Value(Array.get values 0, valueType)
                valueType, getValue
            else
                let tupleItemTypes = 
                    schema.Rows
                    |> Seq.cast<DataRow>
                    |> Seq.skip 1
                    |> Seq.map ( fun row -> Type.GetType( typeName = string row.["DataType"]))

                let tupleType = tupleItemTypes |> Seq.toArray |> FSharpType.MakeTupleType
                let getValue = fun values -> (values, tupleItemTypes) ||> Seq.zip |> Seq.map Expr.Value |> Seq.toList |> Expr.NewTuple
                                
                tupleType, getValue

        let nameAndValuePairs = 
            [ 
                while reader.Read() do 
                    let rowValues = Array.zeroCreate reader.FieldCount
                    let count = reader.GetValues( rowValues)
                    assert (count = rowValues.Length)
                    let label = string rowValues.[0]
                    let tailAsValue = Array.sub rowValues 1 (count - 1) |> getValue
                    yield label, tailAsValue
            ] 

        for name, value in nameAndValuePairs do
            let property = ProvidedProperty( name, valueType, IsStatic = true, GetterCode = fun _ -> value)
            providedEnumType.AddMember( property)
    
        let getNames = ProvidedMethod( "GetNames", [], typeof<string[]>, IsStaticMethod = true)
        getNames.InvokeCode <- fun _ -> Expr.NewArray( typeof<string>, nameAndValuePairs |> List.map (fst >> Expr.Value))
        providedEnumType.AddMember getNames

        let getValues = ProvidedMethod( "GetValues", [], valueType.MakeArrayType(), IsStaticMethod = true)
        getValues.InvokeCode <- fun _ -> Expr.NewArray( valueType, nameAndValuePairs |> List.map snd)
        
        providedEnumType.AddMember getValues

        providedEnumType
