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
    }
}
