namespace PetaPoco.DBEntityGenerator
{
    using PetaPoco.DBEntityGenerator.Outputs;
    using PetaPoco.DBEntityGenerator.SchemaReaders;
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;

    public class Generator
    {
        private IOutput _outer;

        public Generator(IOutput outer)
        {
            _outer = outer;
        }

        public void Generate(GenerateCommand cmd)
        {
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

            foreach (var item in tables)
            {
                if (item.Ignore) { continue; }

                _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.DataAccess\Benchmark\{item.ClassName}.cs");
                var newTables = new Tables { item };

                ApplyTableConfigurations(context, newTables);

                WriteFileStarting(context);
                WriteRecoredClass(context);
                WriteTables(context, newTables);

                WriteFileEnding();

                GenerateModelHelper(item);
                GenerateQueryModelOption(item);
                GenerateModelController(item);
                GenerateModelView(item);
                GenerateQueryModelJs(item);
            }
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
                            {
                                c.PropertyName = "_" + c.PropertyName;
                            }
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

            WriteLine("using System;");
            WriteLine("using System.Collections.Generic;");
            WriteLine("using System.Linq;");
            WriteLine("using PetaPoco;");
            WriteLine("");
            WriteLine("namespace {0}", cmd.Namespace);
            WriteLine("{");
        }

        private void WriteFileEnding()
        {
            WriteLine("}");
            WriteLine("");
        }

        private void WriteRecoredClass(GenerateContext context)
        {
            var cmd = context.Command;

            if (cmd.TrackModifiedColumns)
            {
                WriteLine("    public class Record<T> where T : new()");
                WriteLine("    {");

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
                WriteLine("    }");
                WriteLine("");
            }
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

            WriteLine("    /// <summary>{0}    /// {1}{0}    /// </summary>", Environment.NewLine, tbl.Comment);
            WriteLine("    [TableName(\"{0}\")]", context.EscapeSqlIdentifier(tbl.Name).Replace("\"", "\\\"").Replace("`", string.Empty));

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

            foreach (var col in tbl.Columns.Where(x => !x.Ignore))
            {
                var columnParts = new List<string>();
                columnParts.Add(string.Format("\"{0}\"", col.Name));

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
                    WriteLine("        /// <summary>{0}        /// {1}{0}        /// </summary>", Environment.NewLine, col.Comment);
                    if (new[] { "update_time", "create_time" }.Contains(col.Name))
                    {
                        WriteLine("        [ResultColumn({0}, IncludeInAutoSelect.Yes)]", string.Join(", ", columnParts));
                    }
                    else
                    {
                        WriteLine("        [Column({0})]", string.Join(", ", columnParts));
                    }
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
                    var nullFlag = Helpers.CheckNullable(col);
                    if (nullFlag == "?")
                    {
                        WriteLine("        public {0}{1} {2} {{ get; set; }}", col.PropertyType, nullFlag, col.PropertyName);
                    }
                    else
                    {
                        WriteLine("        public {0}{1} {2} {{ get; set; }} = \"\";", col.PropertyType, nullFlag, col.PropertyName);
                    }
                }

                WriteLine("");
            }

            WriteLine("        /// <summary>{0}        /// 查询所有{0}        /// </summary>{0}        /// <returns></returns>", Environment.NewLine);
            WriteLine("        public virtual IEnumerable<{0}> Retrieves() => DbManager.Create().Query<{0}>();", tbl.ClassName);
            WriteLine("");

            WriteLine("        /// <summary>{0}        /// 保存{0}        /// </summary>{0}        /// <param name=\"v\"></param>{0}        /// <returns></returns>", Environment.NewLine);
            WriteLine(@"        public virtual bool Save({0} v)
        {{
            DbManager.Create().Save(v);
            return true;
        }}", tbl.ClassName);
            WriteLine("");

            WriteLine("        /// <summary>{0}        /// 删除{0}        /// </summary>{0}        /// <param name=\"value\"></param>{0}        /// <returns></returns>", Environment.NewLine);
            WriteLine(@"        public virtual bool Delete(IEnumerable<string> value)
        {{
            if (!value.Any()) return true;

            DbManager.Create().Delete<{0}>($""WHERE id IN(@values)"", new {{ values = value }});
            return true;
        }}", tbl.ClassName);

            WriteLine("    }");
        }

        #endregion

        private void GenerateModelHelper(Table item)
        {
            _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.DataAccess\BenchmarkHelper\{item.ClassName}Helper.cs");

            var temp = File.ReadAllText(@".\Templates\ModelHelper.txt");
            WriteLine(temp, item.ClassName);
        }

        private void GenerateQueryModelOption(Table item)
        {
            _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.Admin\Query\Query{item.ClassName}Option.cs");

            var temp = File.ReadAllText(@".\Templates\QueryModelOption.txt");
            string firstSection = "", secondSection = "", thirdSection = "", fourthSection = "", fifthSection = "";

            foreach (var col in item.Columns.Where(x => !x.Ignore))
            {
                var propertyName = string.Concat(col.PropertyName.Select((c, index) =>
                {
                    if (index == 0) { c = char.ToLower(c); }
                    return c;
                }));

                if (col.Name.Contains("id")) { continue; }

                if (col.PropertyType == "string")
                {
                    firstSection += string.Format("        private {0}? {1};{2}", col.PropertyType, propertyName, Environment.NewLine);
                    secondSection += string.Format("        /// <summary>{0}        /// {1}{0}        /// </summary>{0}", Environment.NewLine, col.Comment);
                    secondSection += string.Format("        public {0}? {1} {{ get => {2}; set => {2} = value?.Trim(); }}", col.PropertyType, col.PropertyName, propertyName);
                    secondSection += Environment.NewLine + Environment.NewLine;
                }
                else
                {
                    secondSection += string.Format("        /// <summary>{0}        /// {1}{0}        /// </summary>{0}", Environment.NewLine, col.Comment);
                    secondSection += string.Format("        public {0}? {1} {{ get; set; }}", col.PropertyType, col.PropertyName);
                    secondSection += Environment.NewLine + Environment.NewLine;
                }

                //
                if (col.PropertyType == "string")
                {
                    thirdSection += string.Format("            if (!string.IsNullOrEmpty({0})){1}", col.PropertyName, Environment.NewLine);
                    thirdSection += string.Format("            {{{0}", Environment.NewLine);
                    thirdSection += string.Format("                data = data.Where(t => t.{0}.Contains({0}, StringComparison.OrdinalIgnoreCase));{1}", col.PropertyName, Environment.NewLine);
                    thirdSection += string.Format("            }}") + Environment.NewLine;

                    if (!fourthSection.Contains("data.Where"))
                    {
                        fourthSection += string.Format("data.Where(t => t.{0}.Contains(Search, StringComparison.OrdinalIgnoreCase)", col.PropertyName) + Environment.NewLine;
                    }
                    else
                    {
                        fourthSection += string.Format("                || t.{0}.Contains(Search, StringComparison.OrdinalIgnoreCase)", col.PropertyName) + Environment.NewLine;
                    }
                }
                else
                {
                    thirdSection += string.Format("            if ({0} != null){1}", col.PropertyName, Environment.NewLine);
                    thirdSection += string.Format("            {{{0}", Environment.NewLine);
                    thirdSection += string.Format("                data = data.Where(t => t.{0} == {0});{1}", col.PropertyName, Environment.NewLine);
                    thirdSection += string.Format("            }}") + Environment.NewLine;
                }

                //
                if (new string[] { "remark" }.Contains(col.Name)) { continue; }
                fifthSection += string.Format("                case \"{0}\":{1}", col.PropertyName, Environment.NewLine);
                fifthSection += string.Format("                    data = Order == \"asc\" ? data.OrderBy(t => t.{0}) : data.OrderByDescending(t => t.{0});{1}", col.PropertyName, Environment.NewLine);
                fifthSection += "                    break;" + Environment.NewLine;
            }

            WriteLine(temp, item.ClassName, firstSection, secondSection, thirdSection, fourthSection.TrimEnd(Environment.NewLine.ToCharArray()), fifthSection);
        }

        private void GenerateModelController(Table item)
        {
            _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.Admin\Controllers\BenchmarkApi\{item.ClassName}sController.cs");

            var temp = File.ReadAllText(@".\Templates\ModelController.txt");
            WriteLine(temp, item.ClassName);
        }

        private void GenerateModelView(Table item)
        {
            _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.Admin\Views\Basis\{item.ClassName}s.cshtml");

            var temp = File.ReadAllText(@".\Templates\ModelView.txt");
            string firstSection = "", secondSection = "";

            foreach (var col in item.Columns.Where(x => !x.Ignore))
            {
                var propertyName = string.Concat(col.PropertyName.Select((c, index) =>
                {
                    if (index == 0) { c = char.ToLower(c); }
                    return c;
                }));

                switch (col.PropertyType)
                {
                    case "string":
                        if (col.Name.Contains("id")) { break; }

                        if (col.Name == "remark")
                        {
                            secondSection += "                <div class=\"form-group col-sm-12\">" + Environment.NewLine;
                            secondSection += string.Format("                    <label class=\"control-label\" for=\"{0}\">{1}</label>{2}", propertyName, col.Comment, Environment.NewLine);
                            secondSection += string.Format("                    <textarea id=\"remark\" class=\"form-control flex-sm-fill\" rows=\"3\" placeholder=\"不可为空，{1}字以内\" maxlength=\"{1}\" data-valid=\"true\"></textarea>{2}", propertyName, col.Length, Environment.NewLine);
                            secondSection += "                </div>" + Environment.NewLine;
                        }
                        else
                        {
                            firstSection += "            <div class=\"form-group col-12\">" + Environment.NewLine;
                            firstSection += string.Format("                <label class=\"control-label\" for=\"txt_{0}\">{1}</label>{2}", propertyName, col.Comment, Environment.NewLine);
                            firstSection += string.Format("                <input type=\"text\" class=\"form-control\" id=\"txt_{0}\" data-default-val=\"\" />{1}", propertyName, Environment.NewLine);
                            firstSection += "            </div>" + Environment.NewLine;

                            secondSection += "                <div class=\"form-group col-sm-6\">" + Environment.NewLine;
                            secondSection += string.Format("                    <label class=\"control-label\" for=\"{0}\">{1}</label>{2}", propertyName, col.Comment, Environment.NewLine);
                            secondSection += string.Format("                    <input type=\"text\" class=\"form-control\" id=\"{0}\" placeholder=\"不可为空，{1}字以内\" maxlength=\"{1}\" data-valid=\"true\" />{2}", propertyName, col.Length, Environment.NewLine);
                            secondSection += "                </div>" + Environment.NewLine;
                        }

                        break;
                    case "bool":
                        firstSection += "            <div class=\"form-group col-12\">" + Environment.NewLine;
                        firstSection += string.Format("                <label class=\"control-label\" for=\"txt_{0}\">{1}</label>{2}", propertyName, col.Comment, Environment.NewLine);
                        firstSection += string.Format("                <input class=\"form-control\" data-toggle=\"lgbSelect\" data-default-val=\"\" />{0}", Environment.NewLine);
                        firstSection += string.Format("                <select data-toggle=\"lgbSelect\" class=\"d-none\" id=\"txt_{0}\">{1}", propertyName, Environment.NewLine);
                        firstSection += "                    <option value=\"\">全部</option>" + Environment.NewLine;
                        firstSection += "                    <option value=\"true\">启用</option>" + Environment.NewLine;
                        firstSection += "                    <option value=\"false\">禁用</option>" + Environment.NewLine;
                        firstSection += "                </select>" + Environment.NewLine;
                        firstSection += "            </div>" + Environment.NewLine;

                        secondSection += "                <div class=\"form-group col-sm-6\">" + Environment.NewLine;
                        secondSection += string.Format("                    <label class=\"control-label\" for=\"{0}\">{1}</label>{2}", propertyName, col.Comment, Environment.NewLine);
                        secondSection += string.Format("                    <select data-toggle=\"lgbSelect\" data-bool=\"true\" class=\"d-none\" data-default-val=\"true\" id=\"{0}\">{1}", propertyName, Environment.NewLine);
                        secondSection += "                        <option value=\"true\">启用</option>" + Environment.NewLine;
                        secondSection += "                        <option value=\"false\">禁用</option>" + Environment.NewLine;
                        secondSection += "                    </select>" + Environment.NewLine;
                        secondSection += "                </div>" + Environment.NewLine;
                        break;
                    default:
                        break;
                }
            }
            var className = string.Concat(item.ClassName.Select((c, index) =>
            {
                if (index == 0) { c = char.ToLower(c); }
                return c;
            }));

            WriteLine(temp, className, firstSection.TrimEnd(Environment.NewLine.ToCharArray()), secondSection.TrimEnd(Environment.NewLine.ToCharArray()));
        }

        private void GenerateQueryModelJs(Table item)
        {
            var className = string.Concat(item.ClassName.Select((c, index) =>
            {
                if (index == 0) { c = char.ToLower(c); }
                return c;
            }));

            _outer = new FileOutput($@"D:\Project\NingMeiCode\BenchmarkAdmin\src\admin\Bootstrap.Admin\wwwroot\js\{className}s.js");

            string firstSection = "", secondSection = "", thirdSection = "";

            foreach (var col in item.Columns.Where(x => !x.Ignore))
            {
                var propertyName = string.Concat(col.PropertyName.Select((c, index) =>
                {
                    if (index == 0) { c = char.ToLower(c); }
                    return c;
                }));

                firstSection += string.Format("                {0}: \"#{1}\",", col.PropertyName, propertyName) + Environment.NewLine;

                switch (col.PropertyType)
                {
                    case "string":
                    case "bool":
                        if (col.Name.Contains("id")) { break; }
                        secondSection += string.Format("                    {0}: $('#txt_{1}').val(),", col.PropertyName, propertyName) + Environment.NewLine;
                        break;
                    default:
                        break;
                }

                if (col.Name.Contains("id")) { continue; }
                if (col.PropertyType == "bool")
                {
                    thirdSection += string.Format("                {{ title: \"{0}\", field: \"{1}\", sortable: true, align: \"center\", formatter: StateFormatter }},", col.Comment, col.PropertyName) + Environment.NewLine;
                }
                else
                {
                    thirdSection += string.Format("                {{ title: \"{0}\", field: \"{1}\", sortable: false }},", col.Comment, col.PropertyName) + Environment.NewLine;
                }
            }

            var temp = File.ReadAllText(@".\Templates\Model.js.txt");
            WriteLine(temp, item.ClassName, firstSection.TrimEnd(Environment.NewLine.ToCharArray()).TrimEnd(','), secondSection.TrimEnd(Environment.NewLine.ToCharArray()).TrimEnd(','), thirdSection.TrimEnd(Environment.NewLine.ToCharArray()).TrimEnd(','));
        }

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
