﻿namespace PetaPoco.DBEntityGenerator.SchemaReaders
{
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;

    public class MySqlSchemaReader : SchemaReader
    {
        // SchemaReader.ReadSchema
        public override Tables ReadSchema(DbConnection connection, DbProviderFactory factory)
        {
            var result = new Tables();

            var cmd = factory.CreateCommand();
            cmd.Connection = connection;
            cmd.CommandText = TABLE_SQL;

            //pull the tables in a reader
            using (cmd)
            {
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        Table tbl = new Table
                        {
                            Name = rdr["TABLE_NAME"].ToString(),
                            Schema = rdr["TABLE_SCHEMA"].ToString(),
                            IsView = string.Compare(rdr["TABLE_TYPE"].ToString(), "View", true) == 0
                        };
                        tbl.CleanName = CleanUp(tbl.Name);
                        tbl.ClassName = Inflector.MakeSingular(tbl.CleanName);
                        result.Add(tbl);
                    }
                }
            }


            //this will return everything for the DB
            var schema = connection.GetSchema("COLUMNS");

            //loop again - but this time pull by table name
            foreach (var item in result)
            {
                item.Columns = new List<Column>();

                //pull the columns from the schema
                var columns = schema.Select("TABLE_NAME='" + item.Name + "'");
                foreach (var row in columns)
                {
                    Column col = new Column
                    {
                        Name = row["COLUMN_NAME"].ToString()
                    };
                    col.PropertyName = CleanUp(col.Name);
                    col.PropertyType = GetPropertyType(row);
                    col.IsNullable = row["IS_NULLABLE"].ToString() == "YES";
                    col.IsPK = row["COLUMN_KEY"].ToString() == "PRI";
                    col.IsAutoIncrement = row["extra"].ToString().ToLower().IndexOf("auto_increment") >= 0;

                    item.Columns.Add(col);
                }
            }

            return result;

        }

        private static string GetPropertyType(DataRow row)
        {
            bool bUnsigned = row["COLUMN_TYPE"].ToString().IndexOf("unsigned") >= 0;
            string propType = "string";
            switch (row["DATA_TYPE"].ToString())
            {
                case "bigint":
                    propType = bUnsigned ? "ulong" : "long";
                    break;
                case "int":
                    propType = bUnsigned ? "uint" : "int";
                    break;
                case "smallint":
                    propType = bUnsigned ? "ushort" : "short";
                    break;
                case "guid":
                    propType = "Guid";
                    break;
                case "smalldatetime":
                case "date":
                case "datetime":
                case "timestamp":
                    propType = "DateTime";
                    break;
                case "float":
                    propType = "float";
                    break;
                case "double":
                    propType = "double";
                    break;
                case "numeric":
                case "smallmoney":
                case "decimal":
                case "money":
                    propType = "decimal";
                    break;
                case "bit":
                case "bool":
                case "boolean":
                    propType = "bool";
                    break;
                case "tinyint":
                    propType = bUnsigned ? "byte" : "sbyte";
                    break;
                case "image":
                case "binary":
                case "blob":
                case "mediumblob":
                case "longblob":
                case "varbinary":
                    propType = "byte[]";
                    break;

            }
            return propType;
        }

        private const string TABLE_SQL = @"
            SELECT * 
            FROM information_schema.tables 
            WHERE (table_type='BASE TABLE' OR table_type='VIEW') AND TABLE_SCHEMA=DATABASE()
            ";
    }
}
