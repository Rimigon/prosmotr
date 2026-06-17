using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        foreach (var mod in asm.Modules)
        {
            foreach (var ty in mod.Types)
            {
                if (ty.Name == "User32Wrapper")
                {
                    Console.WriteLine($"\n=== {ty.FullName} ===");
                    foreach (var m in ty.Methods)
                    {
                        Console.WriteLine($"  method: {m.Name} [{m.Attributes}]");
                        if (m.HasBody)
                        {
                            foreach (var ins in m.Body.Instructions.Take(80))
                                Console.WriteLine($"      {ins.Offset:D4} {ins.OpCode} {OperandStr(ins)}");
                        }
                    }
                }
            }
        }
    }
    static string OperandStr(Instruction ins)
    {
        if (ins.Operand == null) return "";
        if (ins.Operand is MethodReference mr) return $"{mr.DeclaringType.FullName}::{mr.Name}";
        if (ins.Operand is FieldReference fr) return $"{fr.DeclaringType.FullName}::{fr.Name}";
        if (ins.Operand is TypeReference tr) return tr.FullName;
        if (ins.Operand is string s) return "\"" + s + "\"";
        return ins.Operand.ToString();
    }
}
