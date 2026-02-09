using System;

namespace BepInExInstaller;
public static class Util
{
    public static void PrintVerbose(string message, MessageType type = MessageType.Info)
        {
            if (Program.verbose)
            {
                switch (type)
                {
                    case MessageType.Info:
                        Console.ForegroundColor = ConsoleColor.Green;
                        break;
                    case MessageType.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case MessageType.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                }
                
                Console.Write(type + ": ");
                Console.ResetColor();
                Console.WriteLine(message);
            }
        }

        public static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("Error: ");
            Console.ResetColor();
            Console.WriteLine(message);
        } 
        public enum MessageType
        {
            Info,
            Warning,
            Error
        }
}