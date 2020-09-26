namespace PetaPoco.DBEntityGenerator.Outputs
{
    using System;

    public class ConsoleOutput : IOutput
    {
        public void WriteLine(string text)
        {
            Console.WriteLine(text);
        }

        public void Dispose()
        {
        }
    }
}
