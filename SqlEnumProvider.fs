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

        providedEnumType.AddMembers [
            while reader.Read() do
                let name = reader.GetString( 0)
                let value = reader.[1]
                let property = ProvidedProperty( name, valueType, IsStatic = true)
                property.GetterCode <- fun _ -> Expr.Value( value, valueType)
                yield property
        ]

        providedEnumType
