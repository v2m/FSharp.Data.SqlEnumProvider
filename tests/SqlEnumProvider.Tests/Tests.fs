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

//type IntMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", connectionString, ApiStyle = ApiStyle.``C#``>

//Failing to compile !!!

//[<Fact>]
//let ``TryParse C#``() = 
//    let succ, result = IntMapping.TryParse("One")
//    Assert.True succ
//    Assert.Equal(IntMapping.One, result)

type EnumMapping = SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)", connectionString, ApiStyle = ApiStyle.Enum>

[<Fact>]
let Enums() = 
    Assert.Equal(1, int EnumMapping.One)
    //fails at runtime with FSharp.Data.Tests.Enums : System.ArgumentException : Type provided must be an Enum.
//Parameter name: enumType
//Stack Trace:
//   at System.Enum.TryParseEnum(Type enumType, String value, Boolean ignoreCase, EnumResult& parseResult)
//   at System.Enum.Parse(Type enumType, String value)
//   at FSharp.Data.Tests.Enums() in C:\Users\mitekm\Documents\GitHub\FSharp.Data.SqlEnumProvider\tests\SqlEnumProvider.Tests\Tests.fs:line 40
//Output:
//  1
    Assert.True(EnumMapping.One = (Enum.Parse(typeof<EnumMapping>, "One") |> unbox))

    //uncomment bellow to see compilation to fail with
//Error	1	The type provider 'FSharp.Data.SqlEnumProvider' reported an error: The Enum type should contain one and only one instance field.
//Parameter name: enumType	C:\Users\mitekm\Documents\GitHub\FSharp.Data.SqlEnumProvider\tests\SqlEnumProvider.Tests\Tests.fs	34	20	SqlEnumProvider.Tests
    
    //Assert.Equal<EnumMapping>(enum 1, EnumMapping.One)