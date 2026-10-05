using System;
using System.IO;
using System.Linq;
using System.Text;
using BimPortfolio.Core;
using Newtonsoft.Json;

if (args.Length == 2 && args[0] == "--benchmark") return SpatialBenchmark.Run(args[1]);
if (args.Length != 2) { Console.Error.WriteLine("Usage: BimPortfolio.Demo <sample directory> <output directory> | --benchmark <output.json>"); return 2; }
var input = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
T Read<T>(string name) => JsonConvert.DeserializeObject<T>(File.ReadAllText(Path.Combine(input, name))) ?? throw new InvalidDataException(name);
var graph = Read<TrayGraph>("tray-network.json"); var endpoints = Read<DevicePoint[]>("devices.json");
var snapshot = new RoutingSnapshot(graph, endpoints[0], endpoints.Skip(1).ToList());
var plan = RoutePlanner.Calculate(graph, snapshot.Controller, snapshot.Devices, new RouteOptions { MaxCableMeters = 30 });
File.WriteAllText(Path.Combine(output, "routes.json"), PlanJson.Write(plan));
File.WriteAllText(Path.Combine(output, "routes.csv"), CsvReport.Routes(plan.Routes), new UTF8Encoding(true));
var elements = Read<ElementSnapshot[]>("model.json");
var profile = AuditProfileJson.Read(File.ReadAllText(Path.Combine(input, "acs-profile.json")));
var issues = ProfileAuditor.Inspect(elements, profile);
File.WriteAllText(Path.Combine(output, "audit.csv"), CsvReport.Issues(issues), new UTF8Encoding(true));
File.WriteAllText(Path.Combine(output, "mark-preview.json"), JsonConvert.SerializeObject(ModelAuditor.ProposeMissingMarks(elements), Formatting.Indented));
File.WriteAllText(Path.Combine(output, "schematic.json"), JsonConvert.SerializeObject(SchematicLayout.Build(plan), Formatting.Indented));
File.WriteAllText(Path.Combine(output, "AccessRoute.html"), HtmlReport.Create(snapshot, plan, issues));
foreach (var route in plan.Routes) Console.WriteLine($"{route.Mark}: {route.Status}, cable={route.CableMeters:0.##} m");
Console.WriteLine($"ModelGuard: {elements.Length} elements, {issues.Count} issues. Reports: {output}");
Console.WriteLine($"Open {Path.Combine(output, "AccessRoute.html")} in a browser.");
return 0;
