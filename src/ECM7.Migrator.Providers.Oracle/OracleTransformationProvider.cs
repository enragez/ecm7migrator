using System;
using System.Linq;
using System.Collections.Generic;
using System.Data;

using ECM7.Migrator.Framework;
using Oracle.DataAccess.Client;
using ForeignKeyConstraint = ECM7.Migrator.Framework.ForeignKeyConstraint;

namespace ECM7.Migrator.Providers.Oracle
{
	using Framework.Logging;

	/// <summary>
	/// ��������� ������������� ��� Oracle
	/// </summary>
	public class OracleTransformationProvider : TransformationProvider<OracleConnection>
	{
		/// <summary>
		/// �������������
		/// </summary>
		/// <param name="connection">����������� � ��</param>
		public OracleTransformationProvider(OracleConnection connection)
			: base(connection)
		{
			typeMap.Put(DbType.AnsiStringFixedLength, "CHAR(255)");
			typeMap.Put(DbType.AnsiStringFixedLength, 2000, "CHAR($l)");
			typeMap.Put(DbType.AnsiString, "VARCHAR2(255)");
			typeMap.Put(DbType.AnsiString, 2000, "VARCHAR2($l)");
			typeMap.Put(DbType.AnsiString, 2147483647, "CLOB"); // should use the IType.ClobType
			typeMap.Put(DbType.Binary, "RAW(2000)");
			typeMap.Put(DbType.Binary, 2000, "RAW($l)");
			typeMap.Put(DbType.Binary, 2147483647, "BLOB");
			typeMap.Put(DbType.Boolean, "NUMBER(1,0)");
			typeMap.Put(DbType.Byte, "NUMBER(3,0)");
			typeMap.Put(DbType.Currency, "NUMBER(19,1)");
			typeMap.Put(DbType.Date, "DATE");
			typeMap.Put(DbType.DateTime, "TIMESTAMP(4)");
			typeMap.Put(DbType.Decimal, "NUMBER");
			typeMap.Put(DbType.Decimal, 38, "NUMBER($l, $s)", 2);
			// having problems with both ODP and OracleClient from MS not being able
			// to read values out of a field that is DOUBLE PRECISION
			typeMap.Put(DbType.Double, "BINARY_DOUBLE");
			typeMap.Put(DbType.Guid, "RAW(16)");
			typeMap.Put(DbType.Int16, "NUMBER(5,0)");
			typeMap.Put(DbType.Int32, "NUMBER(10,0)");
			typeMap.Put(DbType.Int64, "NUMBER(18,0)");
			typeMap.Put(DbType.Single, "FLOAT(24)");
			typeMap.Put(DbType.StringFixedLength, "NCHAR(255)");
			typeMap.Put(DbType.StringFixedLength, 2000, "NCHAR($l)");
			typeMap.Put(DbType.String, "NVARCHAR2(255)");
			typeMap.Put(DbType.String, 2000, "NVARCHAR2($l)");
			typeMap.Put(DbType.String, 1073741823, "NCLOB");
			typeMap.Put(DbType.Time, "DATE");

			propertyMap.RegisterProperty(ColumnProperty.Null, String.Empty);
		}

		#region Overrides of SqlGenerator

		public override string NamesQuoteTemplate
		{
			get { return "\"{0}\""; }
		}

		public override string BatchSeparator
		{
			get { return "/"; }
		}

		public override string Default(object defaultValue)
		{
			// convert boolean to number (1, 0)
			if (defaultValue is bool)
			{
				defaultValue = (bool)defaultValue ? 1 : 0;
			}

			return base.Default(defaultValue);
		}

		protected override string GetSqlColumnDef(Column column, bool compoundPrimaryKey)
		{
			ColumnSqlBuilder sqlBuilder = new ColumnSqlBuilder(column, typeMap, propertyMap);

			sqlBuilder.AddColumnName(NamesQuoteTemplate);
			sqlBuilder.AddColumnType(IdentityNeedsType);
			sqlBuilder.AddDefaultValueSql(Default);
			sqlBuilder.AddNotNullSql(NeedsNotNullForIdentity);
			sqlBuilder.AddPrimaryKeySql(compoundPrimaryKey);
			sqlBuilder.AddUniqueSql();

			return sqlBuilder.ToString();
		}

		#endregion

		#region custom sql


		public override void AddForeignKey(string name, 
			string primaryTable, string[] primaryColumns, string refTable, string[] refColumns, 
			ForeignKeyConstraint onDeleteConstraint = ForeignKeyConstraint.NoAction, 
			ForeignKeyConstraint onUpdateConstraint = ForeignKeyConstraint.NoAction)
		{
			Require.AreEqual(onUpdateConstraint, ForeignKeyConstraint.NoAction, "Oracle �� ������������ ��������� ����������");

			List<string> command = new List<string>
				{
					FormatSql("ALTER TABLE {0:NAME}", primaryTable),
					FormatSql("ADD CONSTRAINT {0:NAME}", name),
					FormatSql("FOREIGN KEY ({0:COLS})", primaryColumns.ToList()),
					FormatSql("REFERENCES {0:NAME} ({1:COLS})", refTable, refColumns)
				};

			switch (onDeleteConstraint)
			{
				case ForeignKeyConstraint.Cascade:
					command.Add("ON DELETE CASCADE");
					break;
			}

			string commandText = command.ToSeparatedString(" ");
			ExecuteNonQuery(commandText);
		}

		public override bool IndexExists(string indexName, string tableName)
		{
			string sql =
				("select count(*) from user_indexes " +
				"where INDEX_NAME = '{0}' " +
				"and TABLE_NAME = '{1}'")
				.FormatWith(indexName, tableName);

			int count = Convert.ToInt32(ExecuteScalar(sql));
			return count > 0;
		}

		public override void RemoveIndex(string indexName, string tableName)
		{
			if (!IndexExists(indexName, tableName))
			{
				MigratorLogManager.Log.WarnFormat("Index {0} is not exists", indexName);
				return;
			}

			string sql = FormatSql("DROP INDEX {0:NAME}", indexName);

			ExecuteNonQuery(sql);
		}

		// todo: �������� ����� �� ���������� �������� ����� � ��������� �����������

		protected override string GetSqlAddColumn(string table, string columnSql)
		{
			return FormatSql("ALTER TABLE {0:NAME} ADD ({1})", table, columnSql);
		}

		public override bool ConstraintExists(string table, string name)
		{
			string sql =
				string.Format(
					"SELECT COUNT(constraint_name) FROM user_constraints WHERE constraint_name = '{0}' AND table_name = '{1}'",
					name, table);

			MigratorLogManager.Log.ExecuteSql(sql);
			object scalar = ExecuteScalar(sql);
			return Convert.ToInt32(scalar) == 1;
		}

		public override bool ColumnExists(string table, string column)
		{
			if (!TableExists(table))
				return false;

			string sql =
				string.Format(
					"SELECT COUNT(column_name) FROM user_tab_columns WHERE table_name = '{0}' AND column_name = '{1}'",
					table, column);

			MigratorLogManager.Log.ExecuteSql(sql);
			object scalar = ExecuteScalar(sql);
			return Convert.ToInt32(scalar) == 1;
		}

		public override bool TableExists(string table)
		{
			string sql = string.Format(
				"SELECT COUNT(table_name) FROM user_tables WHERE table_name = '{0}'", table);
			MigratorLogManager.Log.ExecuteSql(sql);
			object count = ExecuteScalar(sql);
			return Convert.ToInt32(count) == 1;
		}

		public override string[] GetTables()
		{
			List<string> tables = new List<string>();

			using (IDataReader reader =
				ExecuteReader("SELECT table_name FROM user_tables"))
			{
				while (reader.Read())
				{
					tables.Add(reader[0].ToString());
				}
			}

			return tables.ToArray();
		}

		protected override string GetSqlChangeColumn(string table, string columnSql)
		{
			return FormatSql("ALTER TABLE {0:NAME} MODIFY ({1})", table, columnSql);
		}

		#endregion
	}
}
