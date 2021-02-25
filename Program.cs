﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace pod.xledger.tsql_scriptdom {
    class Program {
        static async Task Main(string[] args) {
            using (var inputStream = Console.OpenStandardInput())
            using (var outputStream = Console.OpenStandardOutput()) {
                var handler = new PodHandler(inputStream, outputStream);
                await handler.HandleMessages();
            }
        }
    }
}
