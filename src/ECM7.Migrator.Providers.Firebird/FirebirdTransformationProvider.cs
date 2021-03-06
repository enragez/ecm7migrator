﻿using System;
using System.Collections.Generic;
using System.Data;

using ECM7.Migrator.Framework;
using ECM7.Migrator.Providers.Firebird.Internal;
using ECM7.Migrator.Providers.Validation;
using FirebirdSql.Data.FirebirdClient;

namespace ECM7.Migrator.Providers.Firebird
{

	[ProviderValidation(typeof(FbConnection), false)]
	public class FirebirdTransformationProvider : TransformationProvider
	{
		/// <summary>
		/// Инициализация
		/// </summary>
		/// <param name="connection">Подключение к БД</param>
		public FirebirdTransformationProvider(FbConnection connection)
			: base(connection)
		{
			typeMap.Put(DbType.AnsiStringFixedLength, "CHAR(255)");
			typeMap.Put(DbType.AnsiStringFixedLength, short.MaxValue, "CHAR($l)");
			typeMap.Put(DbType.AnsiString, "VARCHAR(255)");
			typeMap.Put(DbType.AnsiString, short.MaxValue, "VARCHAR($l)");
			typeMap.Put(DbType.Binary, "VARCHAR(8000)");
			typeMap.Put(DbType.Binary, 8000, "VARCHAR($l)");
			typeMap.Put(DbType.Boolean, "SMALLINT");
			typeMap.Put(DbType.Byte, "SMALLINT");
			typeMap.Put(DbType.Currency, "DECIMAL(18,4)");
			typeMap.Put(DbType.Date, "TIMESTAMP");
			typeMap.Put(DbType.DateTime, "TIMESTAMP");
			typeMap.Put(DbType.Decimal, "DECIMAL");
			typeMap.Put(DbType.Decimal, 38, "DECIMAL($l, $s)", 2);
			typeMap.Put(DbType.Guid, "CHAR(36)");
			typeMap.Put(DbType.Int16, "SMALLINT");
			typeMap.Put(DbType.Int32, "INTEGER");
			typeMap.Put(DbType.Int64, "BIGINT");
			typeMap.Put(DbType.Single, "DOUBLE PRECISION");
			typeMap.Put(DbType.StringFixedLength, "CHAR(255) CHARACTER SET UNICODE_FSS");
			typeMap.Put(DbType.StringFixedLength, 4000, "CHAR($l) CHARACTER SET UNICODE_FSS");
			typeMap.Put(DbType.String, "VARCHAR(255) CHARACTER SET UNICODE_FSS");
			typeMap.Put(DbType.String, 4000, "VARCHAR($l) CHARACTER SET UNICODE_FSS");
			typeMap.Put(DbType.Time, "TIMESTAMP");
		}

		#region Особенности СУБД

		public override string BatchSeparator
		{
			get { return "/"; }
		}

		#endregion

		#region override SqlRunner methods

		protected override IDataReader OpenDataReader(IDbCommand cmd)
		{
			return new InternalDataReader(cmd);
		}

		#endregion

		#region generate sql

		protected override string GetSqlDefaultValue(object defaultValue)
		{
			if (defaultValue is bool)
			{
				defaultValue = ((bool)defaultValue) ? 1 : 0;
			}

			return String.Format("DEFAULT {0}", defaultValue);
		}

		protected override string GetSqlRemoveIndex(string indexName, SchemaQualifiedObjectName tableName)
		{
			return FormatSql("DROP INDEX {0:NAME}", indexName);
		}

		protected override string GetSqlAddColumn(SchemaQualifiedObjectName table, string columnSql)
		{
			return FormatSql("ALTER TABLE {0:NAME} ADD {1}", table.Name, columnSql);
		}

		protected override string GetSqlRenameColumn(SchemaQualifiedObjectName tableName, string oldColumnName, string newColumnName)
		{
			return FormatSql("ALTER TABLE {0:NAME} ALTER COLUMN {1:NAME} TO {2:NAME}",
				tableName.Name, oldColumnName, newColumnName);
		}

		protected override string GetSqlRemoveColumn(SchemaQualifiedObjectName table, string column)
		{
			return FormatSql("ALTER TABLE {0:NAME} DROP {1:NAME} ", table.Name, column);
		}

		public override string GetSqlColumnDef(Column column, bool compoundPrimaryKey)
		{
			var sqlBuilder = new ColumnSqlBuilder(column, typeMap, propertyMap, GetQuotedName);

			sqlBuilder.AppendColumnName();
			sqlBuilder.AppendColumnType(IdentityNeedsType);
			sqlBuilder.AppendDefaultValueSql(GetSqlDefaultValue);
			sqlBuilder.AppendNotNullSql(NeedsNotNullForIdentity);
			sqlBuilder.AppendPrimaryKeySql(compoundPrimaryKey);
			sqlBuilder.AppendUniqueSql();

			return sqlBuilder.ToString();
		}

		#region ChangeColumn

		protected override string GetSqlChangeColumnType(SchemaQualifiedObjectName table, string column, ColumnType columnType)
		{
			string sqlColumnType = typeMap.Get(columnType);

			return FormatSql("ALTER TABLE {0:NAME} ALTER COLUMN {1:NAME} TYPE {2}", table.Name, column, sqlColumnType);
		}

		protected override string GetSqlChangeNotNullConstraint(SchemaQualifiedObjectName table, string column, bool notNull, ref string sqlChangeColumnType)
		{
			const string SQL_TEMPLATE = "UPDATE RDB$RELATION_FIELDS SET RDB$NULL_FLAG = {0} WHERE RDB$FIELD_NAME = '{1}' AND RDB$RELATION_NAME = '{2}';";

			string sqlNotNull = notNull ? "1" : "NULL";

			return FormatSql(SQL_TEMPLATE, sqlNotNull, column, table.Name);
		}

		#endregion

		#endregion

		#region DDL

		public override SchemaQualifiedObjectName[] GetTables(string schema = null)
		{
			string sql = FormatSql(
				"select rdb$relation_name from rdb$relations where rdb$system_flag = 0");

			var result = new List<SchemaQualifiedObjectName>();

			using (IDataReader reader = ExecuteReader(sql))
			{
				while (reader.Read())
				{
					string tableName = reader.GetString(0).Trim();
					result.Add(new SchemaQualifiedObjectName { Name = tableName });
				}
			}

			return result.ToArray();
		}

		public override bool ColumnExists(SchemaQualifiedObjectName table, string column)
		{
			string sql = FormatSql(
				"select count(*) from rdb$relation_fields " +
				"where rdb$relation_name = '{0}' and rdb$field_name = '{1}'", table.Name, column);

			int cnt = Convert.ToInt32(ExecuteScalar(sql));
			return cnt > 0;
		}

		public override bool TableExists(SchemaQualifiedObjectName table)
		{
			string sql = FormatSql(
				"select count(*) from rdb$relations " +
				"where rdb$system_flag = 0 and rdb$relation_name = '{0}'", table.Name);

			int cnt = Convert.ToInt32(ExecuteScalar(sql));
			return cnt > 0;
		}

		public override bool IndexExists(string indexName, SchemaQualifiedObjectName tableName)
		{
			string sql = FormatSql(
				"select count(*) from rdb$indices " +
				"where rdb$relation_name = '{0}' and rdb$index_name = '{1}' " +
				"and not (rdb$index_name starting with 'rdb$')", tableName.Name, indexName);

			int cnt = Convert.ToInt32(ExecuteScalar(sql));
			return cnt > 0;
		}

		public override bool ConstraintExists(SchemaQualifiedObjectName table, string name)
		{
			string sql = FormatSql(
				"select count(*) from rdb$relation_constraints " +
				"where rdb$relation_name = '{0}' and rdb$constraint_name = '{1}'", table.Name, name);

			int cnt = Convert.ToInt32(ExecuteScalar(sql));
			return cnt > 0;
		}

		public override void RenameTable(SchemaQualifiedObjectName oldName, string newName)
		{
			throw new NotSupportedException("Firebird не поддерживает переименование таблиц");
		}

		#endregion
	}
}
