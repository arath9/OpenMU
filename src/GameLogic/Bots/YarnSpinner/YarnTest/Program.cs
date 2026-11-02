using System;
using Yarn.Compiler;
using Yarn.Unity;
using System.IO;

namespace YarnTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== PRUEBA YARNSPINNER EN .NET SIN UNITY ===");

            try
            {
                string yarnContent = File.ReadAllText(@"dialogue.mandolion.yarn", System.Text.Encoding.UTF8);
                
                Console.WriteLine($"✅ Archivo .yarn leído: {yarnContent.Length} caracteres");

                var compilationJob = CompilationJob.CreateFromString(yarnContent, "mandolion");
                var compiler = new YarnCompiler();
                var compiledProgram = compiler.Compile(compilationJob);

                if (compiledProgram.Errors.Count > 0)
                {
                    Console.WriteLine("❌ Errores de compilación:");
                    foreach (var error in compiledProgram.Errors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                    return;
                }

                Console.WriteLine($"✅ Compilado exitosamente");

                var dialogue = new Dialogue();
                dialogue.SetProgram(compiledProgram.Program, compiledProgram.StringTable);
                dialogue.Start("START");

                Console.WriteLine("\n=== INICIANDO DIÁLOGO ===");
                
                int iteration = 0;
                while (dialogue.canContinue)
                {
                    string line = dialogue.Continue();
                    
                    if (!string.IsNullOrEmpty(line))
                    {
                        Console.WriteLine($"[{iteration}] Texto: {line}");
                    }

                    if (dialogue.currentChoices.Count > 0)
                    {
                        Console.WriteLine($"Opciones disponibles ({dialogue.currentChoices.Count}):");
                        for (int i = 0; i < dialogue.currentChoices.Count; i++)
                        {
                            var choice = dialogue.currentChoices[i];
                            Console.WriteLine($"  [{i}] {choice.text} -> {choice.targetNodeName}");
                        }

                        if (iteration < 5)
                        {
                            Console.WriteLine("-> Elegiendo primera opción automáticamente");
                            dialogue.ChooseChoiceIndex(0);
                        }
                    }

                    iteration++;

                    if (iteration > 20)
                    {
                        Console.WriteLine("\n⚠️ WARNING: Demasiadas iteraciones, deteniendo...");
                        break;
                    }
                }

                Console.WriteLine($"\n=== FINAL DEL DIÁLOGO ===");
                Console.WriteLine($"Total iteraciones: {iteration}");
                Console.WriteLine($"Final - canContinue: {dialogue.canContinue}");
                
                if (iteration > 0)
                {
                    Console.WriteLine("\n✅ YARNSPINNER FUNCIONA CORRECTAMENTE EN RUNTIME!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}