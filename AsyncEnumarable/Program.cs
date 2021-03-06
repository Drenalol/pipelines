﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AsyncEnumarable
{
    internal static class Program
    {
        private static bool _stop;
        private static readonly Random Rnd = new Random();
        private static List<Model> _models;

        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            Formatting = Formatting.Indented,
            Converters = new List<JsonConverter> {new IpAddressJson()}
        };

        private static async Task Main()
        {
            _models = JsonConvert.DeserializeObject<List<Model>>(File.ReadAllText("MOCK_DATA.json"), JsonSerializerSettings);
            Console.CancelKeyPress += (sender, args) => _stop = true;

            var pipe = new Pipe();
            var reader = pipe.Reader;
            var writer = pipe.Writer;

#pragma warning disable 4014
            Task.Run(() => Read(reader));
#pragma warning restore 4014

            await foreach (var data in NetworkStreamEmulator())
            {
                await writer.WriteAsync(data);
            }
        }

        private static async Task Read(PipeReader pipeReader)
        {
            var lengthComplete = false;
            uint length = 0;
            var bodyComplete = false;
            string body = null;

            while (!_stop)
            {
                var read = await pipeReader.ReadAsync();

                if (!lengthComplete && read.Buffer.Length >= 4)
                {
                    var headerRawData = read.Buffer.Slice(0, 4);
                    length = BitConverter.ToUInt32(headerRawData.FirstSpan);
                    pipeReader.AdvanceTo(read.Buffer.GetPosition(4));
                    lengthComplete = true;
                }
                else if (lengthComplete && read.Buffer.Length >= length)
                {
                    var bodyRawData = read.Buffer.Slice(0, length);
                    body = Encoding.UTF8.GetString(bodyRawData.FirstSpan);
                    pipeReader.AdvanceTo(read.Buffer.GetPosition(length));
                    bodyComplete = true;
                }
                else if (lengthComplete)
                {
                    // Need more data, so we move forward cursor
                    pipeReader.AdvanceTo(read.Buffer.Start, read.Buffer.GetPosition(read.Buffer.Length));
                }
                else
                {
                    Console.WriteLine("Got wrong data, move position to +1");
                    pipeReader.AdvanceTo(read.Buffer.GetPosition(1));
                }

                if (!lengthComplete || !bodyComplete)
                    continue;
                
                var model = JsonConvert.DeserializeObject<Model>(body);
                Console.WriteLine($"Got message: {body}, length: {length}, deserialize: {(model != null ? "OK" : "Failed")}");
                    
                lengthComplete = false;
                bodyComplete = false;
            }
        }

        private static async IAsyncEnumerable<byte[]> NetworkStreamEmulator()
        {
            while (!_stop)
            {
                var model = _models[Rnd.Next(_models.Count - 1)];
                var modeAsString = JsonConvert.SerializeObject(model);
                var modelInBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(modeAsString));

                var modelBytesLength = modelInBytes.Length;
                var bytesSliced = 0;

                // Send Header with modelBytesLength
                yield return BitConverter.GetBytes((uint) modelBytesLength);

                // Send model body
                while (true)
                {
                    if (modelBytesLength - bytesSliced < bytesSliced)
                    {
                        yield return modelInBytes.Slice(bytesSliced, modelBytesLength - bytesSliced).ToArray();
                        break;
                    }

                    var count = Rnd.Next(bytesSliced, modelBytesLength - bytesSliced);
                    yield return modelInBytes.Slice(bytesSliced, count).ToArray();
                    bytesSliced += count;

                    await Task.Delay(Rnd.Next(15));
                }
            }
        }
    }
}