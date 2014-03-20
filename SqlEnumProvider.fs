namespace FSharp.Data

open System.Reflection
open System.Collections.Generic
open System.Data.SqlClient
open System.Data
open System

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.Quotations

open Samples.FSharp.ProvidedTypes

[<assembly:TypeProviderAssembly()>]
do()

[<TypeProvider>]
type public SqlCommandProvider(config : TypeProviderConfig) as this = 
    inherit TypeProviderForNamespaces()

    let nameSpace = this.GetType().Namespace
    let assembly = Assembly.GetExecutingAssembly()
    let providerType = ProvidedTypeDefinition(assembly, nameSpace, "SqlEnum", Some typeof<obj>, HideObjectMethods = true)

    let cache = Dictionary()

    do 
        providerType.DefineStaticParameters(
            parameters = [ 
                ProvidedStaticParameter("Query", typeof<string>) 
                ProvidedStaticParameter("ConnectionString", typeof<string>) 
            ],             
            instantiationFunction = this.CreateType
        )

        this.AddNamespace( nameSpace, [ providerType ])
    
    member internal this.CreateType typeName parameters = 
        let query : string = unbox parameters.[0] 
        let connectionString : string = unbox parameters.[1] 

        let providedEnumType = ProvidedTypeDefinition(assembly, nameSpace, typeName, baseType = Some typeof<obj>, HideObjectMethods = true)
        
        use conn = new SqlConnection( connectionString)
        conn.Open()

        use cmd = new SqlCommand( query, conn)
        use reader = cmd.ExecuteReader()
        if reader.FieldCount <> 2 then failwith "Two columns query result expected: name and value. Receieved %i columns." reader.FieldCount
        let schema = reader.GetSchemaTable()
        let valueType = Type.GetType(typeName = string schema.Rows.[1].["DataType"])

        let nameValuePairs = [ while reader.Read() do yield reader.GetString( 0), reader.[1] ]

        for name, value in nameValuePairs do
            let property = ProvidedProperty( name, valueType, IsStatic = true, GetterCode = fun _ -> Expr.Value( value, valueType))
            providedEnumType.AddMember( property)
    
        let getNames = ProvidedMethod( "GetNames", [], typeof<string[]>, IsStaticMethod = true)
        getNames.InvokeCode <- fun _ -> Expr.NewArray( typeof<string>, [ for name, _ in nameValuePairs -> Expr.Value name ])
        providedEnumType.AddMember getNames

        let getValues = ProvidedMethod( "GetValues", [], valueType.MakeArrayType(), IsStaticMethod = true)
        getValues.InvokeCode <- fun _ -> Expr.NewArray( valueType, [ for _, value in nameValuePairs -> Expr.Value( value, valueType) ])
        providedEnumType.AddMember getValues

        providedEnumType
