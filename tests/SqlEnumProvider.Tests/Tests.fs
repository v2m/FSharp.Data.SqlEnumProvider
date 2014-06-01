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

[<Fact>]
let parse() = 
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("one", ignoreCase = true))
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One", ignoreCase = false))
    Assert.Equal(TinyIntMapping.One, TinyIntMapping.Parse("One"))
    Assert.Throws<ArgumentException>(Assert.ThrowsDelegateWithReturn(fun() -> box (TinyIntMapping.Parse("blah-blah")))) |> ignore
    Assert.Throws<ArgumentException>(Assert.ThrowsDelegateWithReturn(fun() -> box (TinyIntMapping.Parse("one")))) |> ignore

type IntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", connectionString, ApiStyle = ApiStyle.``C#``>

//Failing to compile !!!

//[<Fact>]
//let ``TryParse C#``() = 
//    let succ, result = IntMapping.TryParse("One")
//    Assert.True succ
//    Assert.Equal(IntMapping.One, result)
