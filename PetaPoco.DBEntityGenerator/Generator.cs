﻿namespace PetaPoco.DBEntityGenerator
{
    using PetaPoco.DBEntityGenerator.SchemaReaders;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class Generator
    {
        private IOutput _outer;

        public Generator(IOutput outer)
        {
            this._outer = outer;
        }

        public void Generate(GenerateCommand cmd)
        {
            WriteLine("// <auto-generated />");
            WriteLine("// This file was automatically generated by the PetePocoGenerator");
            WriteLine("// ");
            WriteLine("// The following connection settings were used to generate this file");
            WriteLine("// ");
            WriteLine("//     Connection String: `{0}`", Helpers.zap_password(cmd.ConnectionString));
            WriteLine("//     Provider:               `{0}`", cmd.ProviderName);
            WriteLine("");

            var context = new GenerateContext
            {
                Command = cmd
            };

            var tables = LoadTables(context);
            ApplyTableConfigurations(context, tables);

            WriteFileStarting(context);
            WriteRecoredClass(context);
            WriteTables(context, tables);

            WriteFileEnding();
        }

        private Tables LoadTables(GenerateContext context)
        {
            var cmd = context.Command;

            if (string.IsNullOrWhiteSpace(cmd.ProviderName))
            {
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// db provider must be provided");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");

                return new Tables();
            }

            if (string.IsNullOrWhiteSpace(cmd.ConnectionString))
            {
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// connection string must be provided.");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");
                return new Tables();
            }

            DbProviderFactory _factory;
            try
            {
                _factory = DbProviderFactories.GetFactory(cmd.ProviderName);
            }
            catch (Exception x)
            {
                var error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// Failed to load provider `{0}` - {1}", cmd.ProviderName, error);
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");
                return new Tables();
            }

            try
            {
                Tables result;
                using (var conn = _factory.CreateConnection())
                {
                    conn.ConnectionString = cmd.ConnectionString;
                    conn.Open();

                    SchemaReader reader = null;

                    if (_factory.GetType().Name == "MySqlClientFactory")
                    {
                        reader = new MySqlSchemaReader();

                        context.EscapeSqlIdentifier = sqlIdentifier => $"`{sqlIdentifier}`";
                    }
                    else if (_factory.GetType().Name == "SqlCeProviderFactory")
                    {
                        reader = new SqlServerCeSchemaReader();
                    }
                    else if (_factory.GetType().Name == "NpgsqlFactory")
                    {
                        reader = new PostGreSqlSchemaReader();

                        context.EscapeSqlIdentifier = sqlIdentifier => $"\"{sqlIdentifier}\"";
                    }
                    else if (_factory.GetType().Name == "OracleClientFactory")
                    {
                        reader = new OracleSchemaReader();
                        context.EscapeSqlIdentifier = sqlIdentifier => $"\"{sqlIdentifier.ToUpperInvariant()}\"";
                    }
                    else
                    {
                        reader = new SqlServerSchemaReader();
                    }

                    reader.outer = _outer;
                    result = reader.ReadSchema(conn, _factory);

                    // Remove unrequired tables/views
                    for (int i = result.Count - 1; i >= 0; i--)
                    {
                        if (cmd.SchemaName != null && string.Compare(result[i].Schema, cmd.SchemaName, true) != 0)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if (!cmd.IncludeViews && result[i].IsView)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if (Helpers.StartsWithAny(result[i].ClassName, cmd.ExcludePrefix))
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                    }

                    conn.Close();

                    var rxClean = new Regex("^(Equals|GetHashCode|GetType|ToString|repo|Save|IsNew|Insert|Update|Delete|Exists|SingleOrDefault|Single|First|FirstOrDefault|Fetch|Page|Query)$");
                    foreach (var t in result)
                    {
                        t.ClassName = cmd.ClassPrefix + t.ClassName + cmd.ClassSuffix;
                        foreach (var c in t.Columns)
                        {
                            c.PropertyName = rxClean.Replace(c.PropertyName, "_$1");

                            // Make sure property name doesn't clash with class name
                            if (c.PropertyName == t.ClassName)
                                c.PropertyName = "_" + c.PropertyName;
                        }
                    }

                    return result;
                }
            }
            catch (Exception x)
            {
                var error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                WriteLine("");
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("// Failed to read database schema - {0}", error);
                WriteLine("// -----------------------------------------------------------------------------------------");
                WriteLine("");

                return new Tables();
            }
        }

        #region Apply Configurations

        private static void ApplyTableConfigurations(GenerateContext context, Tables tables)
        {
            var cmd = context.Command;

            if (cmd.Tables == null)
            {
                return;
            }

            foreach (var tableCfg in cmd.Tables)
            {
                var tableName = tableCfg.Key.ToLower();
                var tableAttribute = tableCfg.Value;
                var existedTable = tables.FirstOrDefault(x => string.Equals(tableName, string.Format("{0}.{1}", x.Schema, x.Name), StringComparison.InvariantCultureIgnoreCase));
                if (existedTable != null)
                {
                    ApplyTableConfiguration(tableAttribute, existedTable);
                }
            }
        }

        private static void ApplyTableConfiguration(GenerateTableCommand tableAttribute, Table existedTable)
        {
            existedTable.Ignore = tableAttribute.Ignore;
            if (!string.IsNullOrWhiteSpace(tableAttribute.ClassName))
            {
                existedTable.ClassName = tableAttribute.ClassName;
            }

            if (tableAttribute.Columns != null)
            {
                foreach (var columnCfg in tableAttribute.Columns)
                {
                    var columnName = columnCfg.Key;
                    var columnAttribute = columnCfg.Value;

                    var existedColumn = existedTable.Columns.FirstOrDefault(x => string.Equals(x.Name, columnName, StringComparison.InvariantCultureIgnoreCase));
                    if (existedColumn != null)
                    {
                        ApplyColumnConfiguration(columnAttribute, existedColumn);
                    }
                }
            }
        }

        private static void ApplyColumnConfiguration(GenerateColumnCommand columnAttribute, Column existedColumn)
        {
            existedColumn.Ignore = columnAttribute.Ignore;
            existedColumn.ForceToUtc = columnAttribute.ForceToUtc;

            if (!string.IsNullOrWhiteSpace(columnAttribute.InsertTemplate))
            {
                existedColumn.InsertTemplate = columnAttribute.InsertTemplate;
            }

            if (!string.IsNullOrWhiteSpace(columnAttribute.UpdateTemplate))
            {
                existedColumn.UpdateTemplate = columnAttribute.UpdateTemplate;
            }

            if (!string.IsNullOrWhiteSpace(columnAttribute.PropertyName))
            {
                existedColumn.PropertyName = columnAttribute.PropertyName;
            }

            if (!string.IsNullOrWhiteSpace(columnAttribute.PropertyType))
            {
                existedColumn.PropertyType = columnAttribute.PropertyType;
            }
        }

        #endregion

        #region Write Files

        private void WriteFileStarting(GenerateContext context)
        {
            var cmd = context.Command;

            WriteLine("// <auto-generated />");
            WriteLine("namespace {0}", cmd.Namespace);
            WriteLine("{");
            WriteLine("    using System;");
            WriteLine("    using System.Collections.Generic;");
            WriteLine("    using System.Linq;");
            WriteLine("");
            WriteLine("    using PetaPoco;");
            WriteLine("");
        }

        private void WriteFileEnding()
        {
            WriteLine("}");
            WriteLine("");
        }

        private void WriteRecoredClass(GenerateContext context)
        {
            var cmd = context.Command;

            WriteLine("    public class Record<T> where T : new()");
            WriteLine("    {");

            if (cmd.TrackModifiedColumns)
            {
                WriteLine("        private Dictionary<string, bool> modifiedColumns = null;");
                WriteLine("        [Ignore]");
                WriteLine("        public Dictionary<string, bool> ModifiedColumns { get { return modifiedColumns; } }");

                WriteLine("        protected void MarkColumnModified(string column_name)");
                WriteLine("        {");
                WriteLine("            if (ModifiedColumns != null)");
                WriteLine("            {");
                WriteLine("                ModifiedColumns[column_name] = true;");
                WriteLine("            }");
                WriteLine("        }");

                WriteLine("");

                WriteLine(@"        private void OnLoaded()
        {
            modifiedColumns = new Dictionary<string,bool>();
        }");
            }

            WriteLine("    }");
            WriteLine("");
        }

        private void WriteTables(GenerateContext context, Tables tables)
        {
            foreach (var item in tables.OrderBy(x => x.Schema).ThenBy(x => x.Name))
            {
                if (item.Ignore)
                {
                    continue;
                }

                WriteTable(context, item);
            }
        }

        private void WriteTable(GenerateContext context, Table item)
        {
            var cmd = context.Command;

            var tbl = item;

            if (string.IsNullOrEmpty(tbl.Schema))
            {
                WriteLine("    [TableName(\"{0}\")]", context.EscapeSqlIdentifier(tbl.Name).Replace("\"", "\\\""));
            }
            else
            {
                WriteLine("    [TableName(\"{0}.{1}\")]", context.EscapeSqlIdentifier(tbl.Schema).Replace("\"", "\\\""), context.EscapeSqlIdentifier(tbl.Name).Replace("\"", "\\\""));
            }

            if (tbl.PK != null && tbl.PK.IsAutoIncrement)
            {
                if (tbl.SequenceName == null)
                {
                    WriteLine("    [PrimaryKey(\"{0}\")]", tbl.PK.Name);
                }
                else
                {
                    WriteLine("    [PrimaryKey(\"{0}\", sequenceName=\"{1}\")]", tbl.PK.Name, tbl.SequenceName);
                }
            }

            if (tbl.PK != null && !tbl.PK.IsAutoIncrement)
            {
                WriteLine("    [PrimaryKey(\"{0}\", AutoIncrement=false)]", tbl.PK.Name);
            }

            if (cmd.ExplicitColumns)
            {
                WriteLine("    [ExplicitColumns]");
            }

            WriteLine("    public partial class {0}", tbl.ClassName);
            if (cmd.TrackModifiedColumns)
            {
                WriteLine("        : Record<{0}>", tbl.ClassName);
            }
            WriteLine("    {");

            foreach (var col in tbl.Columns.OrderBy(x => x.Name).Where(x => !x.Ignore))
            {
                var columnParts = new List<string>();
                if (col.Name != col.PropertyName)
                {
                    columnParts.Add(string.Format("Name = \"{0}\"", col.Name));
                }

                if (col.ForceToUtc)
                {
                    columnParts.Add("ForceToUtc = true");
                }

                if (!string.IsNullOrWhiteSpace(col.InsertTemplate))
                {
                    columnParts.Add(string.Format("InsertTemplate = \"{0}\"", col.InsertTemplate));
                }

                if (!string.IsNullOrWhiteSpace(col.UpdateTemplate))
                {
                    columnParts.Add(string.Format("UpdateTemplate = \"{0}\"", col.UpdateTemplate));
                }

                if (columnParts.Any())
                {
                    WriteLine("        [Column({0})]", string.Join(", ", columnParts));
                }
                else
                {
                    if (cmd.ExplicitColumns)
                    {
                        WriteLine("        [Column]");
                    }
                }

                if (cmd.TrackModifiedColumns)
                {
                    WriteLine("        public {0}{1} {2}", col.PropertyType, Helpers.CheckNullable(col), col.PropertyName);
                    WriteLine("        {");
                    WriteLine("            get {{ return _{0}; }}", col.PropertyName);
                    WriteLine("            set");
                    WriteLine("            {");
                    WriteLine("                _{0} = value;", col.PropertyName); ;
                    WriteLine("                MarkColumnModified(\"{0}\");", col.PropertyName); ;
                    WriteLine("            }");
                    WriteLine("        }");

                    WriteLine("");

                    WriteLine("        private {0}{1} _{2};", col.PropertyType, Helpers.CheckNullable(col), col.PropertyName);
                }
                else
                {
                    WriteLine("        public {0}{1} {2} {{ get; set; }}", col.PropertyType, Helpers.CheckNullable(col), col.PropertyName);
                }

                WriteLine("");
            }

            WriteLine("    }");
            WriteLine("");
        }

        #endregion

        private void WriteLine(params string[] texts)
        {
            if (texts == null)
            {
                return;
            }

            if (texts.Length == 1)
            {
                _outer.WriteLine(texts[0]);
            }
            else
            {
                _outer.WriteLine(string.Format(texts[0], texts.Skip(1).ToArray()));
            }
        }
    }
}