
#r @"bin\Debug\SqlEnumProvider.dll"
#r @"packages\FSharp.Data.SqlClient.1.2.6\lib\net40\FSharp.Data.SqlClient.dll"

open FSharp.Data
open System

[<Literal>]
let adventureWorks = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

//by convention: first column is Name, second is Value
type ShipMethod = SqlEnum<"SELECT Name, ShipMethodID FROM Purchasing.ShipMethod", adventureWorks>

ShipMethod.``CARGO TRANSPORT 5``
ShipMethod.``OVERNIGHT J-FAST``
ShipMethod.GetNames()
ShipMethod.GetValues()

//Now combining 2 F# type providers: SqlEnum and SqlCommandProvider
type OrderHeader = SqlCommandProvider<"
    SELECT *
    FROM Purchasing.PurchaseOrderHeader
    WHERE ShipDate > @shippedLaterThan", adventureWorks>

let cmd = OrderHeader() 

//# of overnight orders after Jan 1, 2008
query {
    for x in cmd.Execute( shippedLaterThan = DateTime( 2008, 1, 1)) do
    where (x.ShipMethodID = ShipMethod.``OVERNIGHT J-FAST``)
    count
} 

