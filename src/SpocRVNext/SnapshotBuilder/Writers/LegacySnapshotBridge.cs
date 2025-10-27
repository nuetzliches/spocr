using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpocR.DataContext.Models;
using SpocR.Services;
using SpocR.SpocRVNext.SnapshotBuilder.Models;
using SpocR.SpocRVNext.Utils;

namespace SpocR.SpocRVNext.SnapshotBuilder.Writers;

internal sealed class LegacySnapshotBridge
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly IConsoleService _console;
    private readonly ISchemaSnapshotService? _legacySnapshotService;

    public LegacySnapshotBridge(IConsoleService console, ISchemaSnapshotService? legacySnapshotService)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _legacySnapshotService = legacySnapshotService;
    }

    public Task WriteAsync(IndexDocument? indexDocument, IReadOnlyList<ProcedureAnalysisResult> updatedProcedures, CancellationToken cancellationToken)
    {
        if (_legacySnapshotService == null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var layoutService = new SchemaSnapshotFileLayoutService();
            var snapshot = layoutService.LoadExpanded();
            if (snapshot == null)
            {
                _console.Verbose("[legacy-bridge] expanded snapshot load returned null; skipping legacy snapshot write");
                return Task.CompletedTask;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Fingerprint) && !string.IsNullOrWhiteSpace(indexDocument?.Fingerprint))
            {
                snapshot.Fingerprint = indexDocument!.Fingerprint;
            }

            if (string.IsNullOrWhiteSpace(snapshot.Fingerprint))
            {
                _console.Verbose("[legacy-bridge] snapshot fingerprint missing; skipping legacy snapshot write");
                return Task.CompletedTask;
            }

            var updatedLookup = BuildUpdatedParameterLookup(updatedProcedures);
            var fallbackLookup = LoadLegacyFallbackSnapshot(snapshot.Fingerprint);

            if (snapshot.Procedures != null)
            {
                foreach (var procedure in snapshot.Procedures)
                {
                    if (procedure == null)
                    {
                        continue;
                    }

                    var key = SnapshotWriterUtilities.BuildKey(procedure.Schema ?? string.Empty, procedure.Name ?? string.Empty);

                    List<SnapshotInput> inputs;
                    if (updatedLookup.TryGetValue(key, out var parameterList))
                    {
                        inputs = ConvertParameters(parameterList);
                    }
                    else if (fallbackLookup.TryGetValue(key, out var legacyInputs))
                    {
                        inputs = legacyInputs.Select(CloneSnapshotInput).ToList();
                    }
                    else if (procedure.Inputs != null && procedure.Inputs.Count > 0)
                    {
                        inputs = procedure.Inputs.Select(CloneSnapshotInput).ToList();
                    }
                    else
                    {
                        inputs = new List<SnapshotInput>();
                    }

                    procedure.Inputs = inputs;
                }
            }

            _legacySnapshotService.Save(snapshot);
            _console.Verbose("[legacy-bridge] legacy snapshot persisted");
        }
        catch (Exception ex)
        {
            _console.Verbose($"[legacy-bridge] failed to persist legacy snapshot: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, IReadOnlyList<StoredProcedureInput>> BuildUpdatedParameterLookup(IReadOnlyList<ProcedureAnalysisResult> updatedProcedures)
    {
        var lookup = new Dictionary<string, IReadOnlyList<StoredProcedureInput>>(StringComparer.OrdinalIgnoreCase);
        if (updatedProcedures == null)
        {
            return lookup;
        }

        foreach (var proc in updatedProcedures)
        {
            var descriptor = proc?.Descriptor;
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.Schema) || string.IsNullOrWhiteSpace(descriptor.Name))
            {
                continue;
            }

            var key = SnapshotWriterUtilities.BuildKey(descriptor.Schema, descriptor.Name);
            lookup[key] = proc?.Parameters ?? Array.Empty<StoredProcedureInput>();
        }

        return lookup;
    }

    private Dictionary<string, List<SnapshotInput>> LoadLegacyFallbackSnapshot(string currentFingerprint)
    {
        var result = new Dictionary<string, List<SnapshotInput>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var projectRoot = ProjectRootResolver.ResolveCurrent();
            var candidateDirs = new[]
            {
                Path.Combine(projectRoot, ".spocr", "cache", "schema"),
                Path.Combine(projectRoot, ".spocr", "schema")
            };

            foreach (var dir in candidateDirs)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    continue;
                }

                var legacyFiles = Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
                    .Where(f => !string.Equals(Path.GetFileName(f), "index.json", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !string.Equals(Path.GetFileNameWithoutExtension(f), currentFingerprint, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ToList();

                foreach (var file in legacyFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var fallback = JsonSerializer.Deserialize<LegacySnapshotDocument>(json, SerializerOptions);
                        if (fallback?.Procedures == null)
                        {
                            continue;
                        }

                        foreach (var proc in fallback.Procedures)
                        {
                            if (proc == null || string.IsNullOrWhiteSpace(proc.Schema) || string.IsNullOrWhiteSpace(proc.Name))
                            {
                                continue;
                            }

                            var key = SnapshotWriterUtilities.BuildKey(proc.Schema, proc.Name);
                            if (!result.ContainsKey(key) && proc.Inputs != null && proc.Inputs.Count > 0)
                            {
                                result[key] = proc.Inputs.Select(CloneSnapshotInput).ToList();
                            }
                        }

                        if (result.Count > 0)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        // continue to next candidate
                    }
                }

                if (result.Count > 0)
                {
                    break;
                }
            }
        }
        catch
        {
            // ignore fallback load errors
        }

        return result;
    }

    private static List<SnapshotInput> ConvertParameters(IReadOnlyList<StoredProcedureInput> parameters)
    {
        var list = new List<SnapshotInput>();
        if (parameters == null)
        {
            return list;
        }

        foreach (var parameter in parameters)
        {
            if (parameter == null)
            {
                continue;
            }

            var typeRef = SnapshotWriterUtilities.BuildTypeRef(parameter);
            var snapshotInput = new SnapshotInput
            {
                Name = SnapshotWriterUtilities.NormalizeParameterName(parameter.Name),
                TypeRef = typeRef,
                IsOutput = parameter.IsOutput ? true : null,
                HasDefaultValue = parameter.HasDefaultValue ? true : null,
                IsNullable = parameter.IsNullable ? true : null,
                MaxLength = parameter.MaxLength > 0 ? parameter.MaxLength : null,
                TableTypeSchema = parameter.IsTableType ? parameter.UserTypeSchemaName : null,
                TableTypeName = parameter.IsTableType ? parameter.UserTypeName : null,
                TypeSchema = !parameter.IsTableType ? parameter.UserTypeSchemaName : null,
                TypeName = !parameter.IsTableType ? parameter.UserTypeName : null,
                Precision = parameter.Precision > 0 ? parameter.Precision : null,
                Scale = parameter.Scale > 0 ? parameter.Scale : null
            };

            if (!parameter.IsTableType && string.IsNullOrWhiteSpace(snapshotInput.TypeName))
            {
                var (_, nameFromRef) = SnapshotWriterUtilities.SplitTypeRef(typeRef);
                snapshotInput.TypeSchema ??= "sys";
                snapshotInput.TypeName = nameFromRef;
            }

            if (parameter.IsTableType && string.IsNullOrWhiteSpace(snapshotInput.TableTypeName))
            {
                var (schemaFromRef, nameFromRef) = SnapshotWriterUtilities.SplitTypeRef(typeRef);
                snapshotInput.TableTypeSchema ??= schemaFromRef;
                snapshotInput.TableTypeName = nameFromRef;
            }

            list.Add(snapshotInput);
        }

        return list;
    }

    private static SnapshotInput CloneSnapshotInput(SnapshotInput source)
    {
        return new SnapshotInput
        {
            Name = source?.Name ?? string.Empty,
            TypeRef = source?.TypeRef,
            TableTypeSchema = source?.TableTypeSchema,
            TableTypeName = source?.TableTypeName,
            IsNullable = source?.IsNullable,
            MaxLength = source?.MaxLength,
            HasDefaultValue = source?.HasDefaultValue,
            IsOutput = source?.IsOutput,
            TypeSchema = source?.TypeSchema,
            TypeName = source?.TypeName,
            Precision = source?.Precision,
            Scale = source?.Scale
        };
    }

    private sealed class LegacySnapshotDocument
    {
        public int SchemaVersion { get; set; }
        public string Fingerprint { get; set; } = string.Empty;
        public List<LegacyProcedureDocument> Procedures { get; set; } = new();
    }

    private sealed class LegacyProcedureDocument
    {
        public string Schema { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<SnapshotInput> Inputs { get; set; } = new();
    }
}
