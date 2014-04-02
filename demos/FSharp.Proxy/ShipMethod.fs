namespace global

open FSharp.Data

//by convention: first column is Name, second is Value
type ShipMethod = SqlEnumProvider<"SELECT Name, ShipMethodID FROM Purchasing.ShipMethod ORDER BY ShipMethodID", @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True">


