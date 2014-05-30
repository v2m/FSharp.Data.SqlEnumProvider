using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Names: {0}", String.Join(",", ShipMethod.Names));
        Console.WriteLine("Value: {0}", String.Join(",", ShipMethod.Values));
        Console.WriteLine("Parse: {0}", ShipMethod.Parse("cARGO TRANSPORT 5", true));
        var result = -1;
        if (ShipMethod.TryParse("CARGO TRANSPORT 5", ref result))
            Console.WriteLine("TryParse1: {0}", result);
        if (ShipMethod.TryParse("cARGO TRANSPORT 5", true, ref result))
            Console.WriteLine("TryParse2: {0}", result);
    }
}
