
open System
open System.Data
open System.Data.SqlClient
let conn = new SqlConnection("""Data Source=(LocalDb)\v11.0;Initial Catalog=AdventureWorks2012;Integrated Security=True""")
conn.Close()
conn.Open()

let cmd = new SqlCommand("SELECT Name, PhoneNumberTypeID FROM Person.PhoneNumberType", conn)
let reader = cmd.ExecuteReader(CommandBehavior.CloseConnection ||| CommandBehavior.SingleRow)
let schema = reader.GetSchemaTable()
String.concat "," [for c in schema.Columns -> c.ColumnName ]
String.concat "\n" [for r in schema.Rows -> sprintf "%A-%A" r.["ColumnName"] r.["DataType"] ]

