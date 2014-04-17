
#r @"..\src\bin\Debug\FSharp.Data.SqlEnumProvider.dll"
#r @"..\packages\FSharp.Data.SqlClient.1.2.6\lib\net40\FSharp.Data.SqlClient.dll"

open FSharp.Data
open System

[<Literal>]
let adventureWorks = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

//by convention: first column is Name, second is Value
type ShipMethod = SqlEnumProvider<"SELECT Name, ShipMethodID, ModifiedDate, SYSDATETIMEOFFSET() AS UTC FROM Purchasing.ShipMethod ORDER BY ShipMethodID", adventureWorks>

ShipMethod.Names
ShipMethod.Values

ShipMethod.``CARGO TRANSPORT 5``
ShipMethod.``OVERNIGHT J-FAST``

//Now combining 2 F# type providers: SqlEnum and SqlCommandProvider
type OrderHeader = SqlCommandProvider<"
    SELECT *
    FROM Purchasing.PurchaseOrderHeader
    WHERE ShipDate > @shippedLaterThan", adventureWorks>

let cmd = OrderHeader() 

//# of overnight orders after Jan 1, 2008
query {
    for x in cmd.Execute( shippedLaterThan = DateTime( 2008, 1, 1)) do
    let shipMethod, _, _ = ShipMethod.``OVERNIGHT J-FAST``
    where (x.ShipMethodID = shipMethod )
    count
} 

ShipMethod.TryParse("CARGO TRANSPORT 5") // Some 5
ShipMethod.TryParse("cargo transport 5") // None
ShipMethod.TryParse("cargo transport 5", ignoreCase = true) // Some 5
ShipMethod.TryParse("Unknown") //None
