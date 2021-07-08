using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace pod.xledger.tsql_scriptdom {
    public class PodHandler {
        Stream _inputStream;
        Stream _outputStream;
        PipeReader _reader;
        PipeWriter _writer;
        BencodeParser _parser;

        public PodHandler(Stream inputStream, Stream outputStream) {
            _inputStream = inputStream;
            _outputStream = outputStream;
            _reader = PipeReader.Create(inputStream);
            _writer = PipeWriter.Create(outputStream, new StreamPipeWriterOptions(leaveOpen: true));
            _parser = new BencodeParser();
        }

        public async Task HandleMessages() {
            var cts = new CancellationTokenSource();
            while (!cts.IsCancellationRequested && _inputStream.CanRead && _outputStream.CanWrite) {
                try {
                    var msg = await _parser.ParseAsync<BDictionary>(_reader, cts.Token);
                    if (msg.TryGetValue("op", out IBObject op)) {
                        var s = ((BString)op).ToString();
                        await HandleMessage(s, msg, cts);
                    }
                } catch (OperationCanceledException) {
                } catch(BencodeNET.Exceptions.BencodeException ex) when /* HACK */ (ex.Message.Contains("but reached end of stream")) {
                    // ^ This message filter appears to be the only way to check if the stream is closed, because
                    // the BencodeNET does not expose an inner exception, specific code, etc when the stream closes.
                    await SendException(null, "Reached end of stream");
                    return;
                } catch (Exception ex) {
                    await SendException(null, ex.Message);
                }
            }
        }

        async Task HandleMessage(string operation, BDictionary msg, CancellationTokenSource cts) {
            switch (operation) {
                case "describe":
                    var resp = new BDictionary {
                        ["format"] = new BString("json"),
                        ["namespaces"] = new BList {
                            new BDictionary {
                                ["name"] = new BString("pod.xledger.tsql-scriptdom"),
                                ["vars"] = new BList {
                                    new BDictionary { ["name"] = new BString("reformat-sql") }
                                }
                             }
                        },
                        ["ops"] = new BDictionary {
                            ["shutdown"] = new BDictionary()
                        }
                    };
                    await resp.EncodeToAsync(_writer);
                    break;
                case "shutdown":
                    cts.Cancel();
                    break;
                case "invoke":
                    await HandleInvoke(msg, cts);
                    break;
                default:
                    break;
            }
        }

        public static string JSON(object o) {
            var s = JsonConvert.SerializeObject(o);
            return s;
        }

        public static JToken ParseJson(string s) {
            var reader = new JsonTextReader(new StringReader(s));

            // We don't need/want NewtonSoft to tamper with our data:
            reader.DateParseHandling = DateParseHandling.None;
            reader.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;

            return JToken.Load(reader);
        }

        public static class StatusMessages {
            public static readonly BList DONE_ERROR = new BList(new[] { "done", "error" });

            public static readonly BList DONE = new BList(new[] { "done" });
        }

        async Task SendException(string id, string exMessage, object exData = null) {
            var resp = new BDictionary {
                ["ex-message"] = new BString(exMessage),
                ["status"] = StatusMessages.DONE_ERROR
            };
            if (id != null) { resp["id"] = new BString(id); }
            if (exData != null) { resp["ex-data"] = new BString(JSON(exData)); }
            await resp.EncodeToAsync(_writer);
        }

        async Task SendResult(string id, object result, bool isJson = false) {
            var json = isJson ? (string)result : JSON(result);
            var resp = new BDictionary {
                ["id"] = new BString(id),
                ["value"] = new BString(json),
                ["status"] = StatusMessages.DONE
            };
            await resp.EncodeToAsync(_writer);
        }

        async Task HandleInvoke(BDictionary msg, CancellationTokenSource cts) {
            if (!(msg.TryGetNonBlankString("id", out var id)
                && msg.TryGetNonBlankString("var", out var varname))) {
                await SendException(id, "Missing \"id\" and/or \"var\" keys in \"invoke\" operation payload");
                return;
            }

            switch (varname) {
                case "pod.xledger.tsql-scriptdom/reformat-sql":
                    await HandleVar_Reformat(id, msg);
                    break;
                default:
                    await SendException(id, $"No such var: \"{varname}\"");
                    break;
            }
        }

        async Task HandleVar_Reformat(string id, BDictionary msg) {
            if (!msg.TryGetValue("args", out var beArgs) || !(beArgs is BString beArgsStr)) {
                await SendException(id, $"Missing required \"args\" argument.");
                return;
            }

            IReadOnlyDictionary<string, JToken> argMap;
            try {
                argMap = JsonConvert.DeserializeObject<IList<IReadOnlyDictionary<string, JToken>>>(beArgsStr.ToString()).First();
            } catch (Exception ex) {
                await SendException(id, $"Couldn't deserialize json payload. Expected a map. Error: {ex.Message}");
                return;
            }

            if (!argMap.TryGetNonBlankString("sql", out var sql)) {
                await SendException(id, $"Missing required \"sql\" argument.");
                return;
            }

            argMap.TryGetBool("initial-quoted-identifiers", out var initialQuotedIdentifiers);

            var parser = new TSql150Parser(initialQuotedIdentifiers);
            var fragment = parser.Parse(new StringReader(sql), out var errors);

            if (errors.Count > 0) {
                await SendException(id, "Could not parse sql", new Dictionary<string, object> { ["errors"] = errors });
                return;
            }

            var scriptGen = new Sql150ScriptGenerator();
            scriptGen.GenerateScript(fragment, out string formattedSql);

            await SendResult(id, formattedSql);
        }
    }

    public class ResultSet {
        public string[] columns;
        public List<object[]> rows;
    }
}
