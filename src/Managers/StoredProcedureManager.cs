using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using SpocR.DataContext.Queries;
using SpocR.Models;

namespace SpocR.Managers
{
    public class StoredProcedureManager : ManagerBase
    {
        public StoredProcedureManager(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        public async Task<List<StoredProcedureModel>> ListAsync(List<SchemaModel> schemaList, ConfigurationModel config, CancellationToken cancellationToken = default)
        {
            var schemaListString = string.Join(',', schemaList.Select(i => i.Id));
            var result = await DbContext.StoredProcedureListAsync(schemaListString, cancellationToken);
            var storedProcedures = result.Select(i => new StoredProcedureModel(i)).ToList();
            foreach (var storedProcedure in storedProcedures)
            {
                storedProcedure.Input = await ListInputAsync(storedProcedure.Id, cancellationToken);
                storedProcedure.Output = await ListOutputAsync(storedProcedure.Id, cancellationToken);

                var output = storedProcedure.Output?.ToList();
                if (output.Count == 1 && output[0].Name.StartsWith("JSON_"))
                {
                    // TODO this is the point to set ResultKindEnum for storedProcedureModel to Json

                    // TODO create a new SourceFileManager for this code
                    // ensure to read the files only once beforce PULL-Command
                    var sqlFileSources = config.Project.Sources?.ToList();
                    if (sqlFileSources.Any())
                    {
                        var sqlFileNames = new List<string>();
                        foreach (var source in sqlFileSources)
                        {
                            var path = Path.GetDirectoryName(source);
                            var searchPattern = Path.GetFileName(source);
                            try
                            {
                                // Directory.GetDirectories(path);
                                sqlFileNames.AddRange(Directory.EnumerateFiles(path, searchPattern, SearchOption.AllDirectories).Where(fileName =>
                                {
                                    // TODO where configure the folder blacklist?
                                    // or is it possible to pass it to the searchPattern?
                                    var blacklist = new[] { @"\bin", @"\obj" };
                                    var filePath = Path.GetDirectoryName(fileName);
                                    var subPath = filePath.Replace(path, "");
                                    var res = !blacklist.Any(blacklistedPath => subPath.Contains(blacklistedPath, StringComparison.InvariantCultureIgnoreCase));
                                    return res;
                                }));


                                foreach (var fileName in sqlFileNames)
                                {
                                    var sql = await File.ReadAllTextAsync(fileName, cancellationToken);
                                    var parseResult = Parser.Parse(sql);

                                    // reduce parser actions
                                    if (!parseResult.Script.Sql.Contains("CREATE PROCEDURE", StringComparison.InvariantCultureIgnoreCase))
                                    {
                                        continue;
                                    }

                                    parseResult.Script.Children.ToList().ForEach(child =>
                                    {
                                        var batch = child as SqlBatch;
                                        if(batch == null) return;
                                        batch.Statements.ToList().ForEach(batchStatement => {
                                            var createProcedureStatement = batchStatement as SqlCreateProcedureStatement;
                                            if(createProcedureStatement == null) return;
                                            // Skip CompoundStatement (BEGINN ... END)
                                            var statements = (createProcedureStatement.Statements[0] as SqlCompoundStatement).Statements;
                                            statements.ToList().ForEach(statement => {
                                                var selectStatement = statement as SqlSelectStatement;
                                                if(selectStatement == null) return;
                                                // TODO we need a Tree Model to reflect the SelectStatements
                                                // then query SqlServer for each leaf to get the field definitions 
                                                // The Tree Model will be the new Output-Model of the SP
                                                
                                                // ! TEST for SP api.CreditorFindById
                                                // Remove last Child (SqlForXmlPathClause :/ SqlForJson is not implemented)
                                                var cleanStatements = selectStatement.SelectSpecification.Children.SkipLast(1);
                                                cleanStatements.ToList().ForEach(cleanStatement => {
                                                    var specification = cleanStatement as SqlQuerySpecification;
                                                    var subselects = specification.SelectClause.Children.ToList().Where(c => (c as SqlSelectScalarExpression).Expression is SqlScalarSubQueryExpression);
                                                
                                                    subselects.ToList().ForEach(async subselect => {

                                                        var subQuery = subselect.Children.ToList()[0] as SqlScalarSubQueryExpression;
                                                        if(subQuery == null) return;
                                                        
                                                        // remove FOR JSON
                                                        var cleanQuery = subQuery.QueryExpression.Children.Where(c => !(c is SqlForXmlPathClause));

                                                        var cleanSql = string.Join(" ", cleanQuery.Select(_ => _.Sql)).Replace("\r", " ").Replace("\n", " ");
                                                        // TODO first we have to remove all query references to outer query
                                                        // set an unknown ref to NULL

                                                        var resultSet = await DbContext.AdHocResultSetListAsync(cleanSql, cancellationToken);
                                                        // TODO get the Field name (or from previous iteration?)
                                                        var identifier = subselect.Children.Last() as SqlLiteralStringIdentifier;

                                                    });
                                                });
                                            });
                                            // var test = batchStatement.GetType();
                                        });
                                    });
                                    // parseResult.Script.Children.Where(_ => _.Statements.Any(__ => __ is SqlCreateStoredProcedureStatement))
                                }
                            }
                            catch (Exception e)
                            {
                                // TODO print reportService warning
                            }
                        }
                    }
                }
            }
            return storedProcedures;
        }

        public async Task<List<StoredProcedureInputModel>> ListInputAsync(int objectId, CancellationToken cancellationToken = default)
        {
            var result = await DbContext.StoredProcedureInputListAsync(objectId, cancellationToken);

            foreach (var input in result.Where(i => i.IsTableType).ToList())
            {
                input.TableTypeColumns = await DbContext.UserTableTypeColumnListAsync(input.UserTypeId ?? -1, cancellationToken);
            }

            return result.Select(i => new StoredProcedureInputModel(i)).ToList();
        }

        public async Task<List<StoredProcedureOutputModel>> ListOutputAsync(int objectId, CancellationToken cancellationToken = default)
        {
            var result = await DbContext.StoredProcedureOutputListAsync(objectId, cancellationToken);
            return result?.Select(i => new StoredProcedureOutputModel(i)).ToList();
        }
    }
}