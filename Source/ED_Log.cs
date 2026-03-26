using System;
using System.IO;
using UnityEngine;

namespace EconomicsDemography
{
    // Название строго Log. Не меняйте ни один другой файл в проекте!
    public static class Log
    {
        public static bool Enabled = false; // Позже привяжете к настройкам
        
        private static string logPath;
        private static bool isInitialized;

        // Ленивая инициализация (создание файла только при первой записи)
        private static void InitIfNeeded()
        {
            if (isInitialized) return;
            logPath = Path.Combine(Application.persistentDataPath, "FinitePopulation.log");
            try { File.WriteAllText(logPath, $"--- Launch: {DateTime.Now} ---\n"); } catch { }
            isInitialized = true;
        }

        public static void Message(string text)
        {
            if (!Enabled) return;
            
            // Если хотите, чтобы логи ТАКЖЕ выводились в обычную консоль игры:
            Verse.Log.Message(text); 
            
            WriteToFile($"[MSG] {text}");
        }

        public static void Warning(string text)
        {
            if (!Enabled) return;
            Verse.Log.Warning(text);
            WriteToFile($"[WARN] {text}");
        }

        public static void Error(string text)
        {
            Verse.Log.Error(text); // Ошибки всегда дублируем в консоль игры
            if (Enabled) WriteToFile($"[ERR] {text}");
        }

        private static void WriteToFile(string text)
        {
            InitIfNeeded();
            try
            {
                File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss} | {text}\n");
            }
            catch { } // Игнорируем ошибки доступа к файлу
        }
		
		public static void ErrorOnce(string text, int key)
{
    // Отправляем в стандартный логгер игры (он сам следит за тем, чтобы вывести это 1 раз)
    Verse.Log.ErrorOnce(text, key); 
    
    // Записываем в наш файл
    if (Enabled) WriteToFile($"[ERR ONCE] {text}");
}
    }
}
