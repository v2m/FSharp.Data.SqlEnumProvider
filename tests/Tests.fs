module FSharp.Data.Tests

open System
open Xunit

[<Literal>]
let connectionString = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type TinyIntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), CAST(1 AS tinyint)), ('Two', CAST(2 AS tinyint))) AS T(Tag, Value)", connectionString>

[<Fact>]
let tinyIntMapping() = 
    Assert.Equal<string[]>([| "One"; "Two" |], TinyIntMapping.Names)
    Assert.Equal<byte[]>([| 1uy; 2uy |], TinyIntMapping.Values)

