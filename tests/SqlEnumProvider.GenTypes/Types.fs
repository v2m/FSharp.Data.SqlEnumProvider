namespace FSharp.Data

type IntMapping = 
    SqlEnumProvider<"SELECT * FROM (VALUES(('One'), 1), ('Two', 2)) AS T(Tag, Value)\
        ", @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True", ApiStyle = ApiStyle.``C#``>

