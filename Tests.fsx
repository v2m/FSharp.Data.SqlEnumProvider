
#r @"bin\Debug\SqlEnumProvider.dll"
#r @"packages\FSharp.Data.SqlClient.1.2.6\lib\net40\FSharp.Data.SqlClient.dll"

open FSharp.Data
open System

[<Literal>]
let adventureWorks = @"Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True"

type ShipMethod = SqlEnum<"SELECT Name, ShipMethodID FROM Purchasing.ShipMethod", adventureWorks>


//Now combining 2 F# type providers: SqlEnum and SqlCommandProvider
type OrderHeader = SqlCommandProvider<"
    SELECT *
    FROM Purchasing.PurchaseOrderHeader
    WHERE ShipDate > @shippedLaterThan", adventureWorks>

let overNight = query {
    let cmd = OrderHeader()
    for x in cmd.Execute( shippedLaterThan = DateTime( 2008, 1, 1)) do
    where (x.ShipMethodID = ShipMethod.``OVERNIGHT J-FAST``)
    select x
}


