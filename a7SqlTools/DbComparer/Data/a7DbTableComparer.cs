﻿using a7SqlTools.DbComparer.Data.Views;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using a7SqlTools.Utils;

namespace a7SqlTools.DbComparer.Data
{
    public class a7DbTableComparer : INotifyPropertyChanged
    {
        public bool IsDifferentData { get; private set; }

        private bool _isTableVisible;

        public bool IsTableVisible
        {
            get { return _isTableVisible; }
            set { _isTableVisible = value; OnPropChanged("IsTableVisible"); }
        }
        

        private bool _isAnalyzedRows;
        public bool IsAnalyzedRows {
            get { return _isAnalyzedRows; }
            private set
            {
                _isAnalyzedRows = value;
                OnPropChanged("IsAnalyzedRows");
            }
        }

        public bool IsDifferentStructure { get; private set; }
        public bool IsSelected { get; set; }
        public bool IsOK { get; private set; }
        public string ErrorText { get; private set; }
        public string TableName { get { return TableA.Name; } }

        public Table TableA { get; private set; }
        public List<string> PrimaryKeyColumnsA { get; private set; }
        public List<string> ColumnsA { get; private set; }
        public Table TableB { get; private set; }
        public List<string> PrimaryKeyColumnsB { get; private set; }
        public List<string> ColumnsB { get; private set; }

        public DataSet DataSetA { get; private set; }
        public DataSet DataSetB { get; private set; }
        public DataTable DataTableA { get; private set; }
        public DataTable DataTableB { get; private set; }

        public string DbAName { get { return _comparer.DbAName; } }
        public string DbBName { get { return _comparer.DbBName; } }

        public ObservableCollection<string> ColumnsOnlyInA { get; private set; }
        public ObservableCollection<string> ColumnsOnlyInB { get; private set; }
        public ObservableCollection<string> ColumnsInBothTables { get; private set; }
        public ICommand AnalyzeTable { get; private set; }
        public ICommand HideTable { get; private set; }
        public ICommand ShowTable { get; private set; }

        private ObservableCollection<a7ComparisonRow> _rows;
        public ObservableCollection<a7ComparisonRow> Rows
        {
            get { return _rows; }
            private set { _rows = value; OnPropChanged("Rows"); }
        }

        private ObservableCollection<DataGridColumn> _columns;
        public ObservableCollection<DataGridColumn> Columns
        {
            get { return _columns; }
            private set
            {
                _columns = value;
                OnPropChanged("Columns");
            }
        }

        public a7DbComparerDirection MergeDirection { get; private set; }

        public ICommand MergeAtoB { get; private set; }
        public ICommand MergeBtoA { get; private set; }
        private bool _mergeWithDelete;
        /// <summary>
        /// if set, on setting merge of whole table - the records that don't exist in the source, will be deleted in the destination
        /// </summary>
        public bool MergeWithDelete { get { return _mergeWithDelete; }
            set
            {
                _mergeWithDelete = value;
                if(MergeDirection!= a7DbComparerDirection.None)
                    SetMergeDirection(MergeDirection);
                _comparer.MergeWithDelete = null;
            }
        }

        private a7DbDataComparer _comparer;
        private Server _srv;

        public a7DbTableComparer(Table tableA, Table tableB, a7DbDataComparer dataComparer)
        {
            IsTableVisible = false;
            IsSelected = true;
            PrimaryKeyColumnsA = new List<string>();
            PrimaryKeyColumnsB = new List<string>();
            ColumnsA = new List<string>();
            ColumnsB = new List<string>();

            _mergeWithDelete = false;
            MergeDirection = a7DbComparerDirection.None;
            MergeAtoB = new a7LambdaCommand((o) =>
            {
                if (MergeDirection != a7DbComparerDirection.AtoB)
                    SetMergeDirection(a7DbComparerDirection.AtoB);
                else
                    SetMergeDirection(a7DbComparerDirection.None);
            }
            );
            MergeBtoA = new a7LambdaCommand((o) =>
            {
                if (MergeDirection != a7DbComparerDirection.BtoA)
                    SetMergeDirection(a7DbComparerDirection.BtoA);
                else
                    SetMergeDirection(a7DbComparerDirection.None);
            }
            );

            AnalyzeTable = new a7LambdaCommand((o) =>
                { analyzeTable(); IsTableVisible = true; });
            HideTable = new a7LambdaCommand((o) => IsTableVisible = false);
            ShowTable = new a7LambdaCommand((o) => IsTableVisible = true);
            dataComparer.Log("Testing for differences - '" + tableA.Name + "'");
            _comparer = dataComparer;
            _srv = dataComparer.Srv;
            TableA = tableA;
            TableB = tableB;
            IsOK = true;

            var sql = "Select * from {0}.dbo.{1}";
            DataSetA = _srv.ConnectionContext.ExecuteWithResults(string.Format(sql, DbAName, tableA.Name));
            DataTableA = DataSetA.Tables[0];

            DataSetB = _srv.ConnectionContext.ExecuteWithResults(string.Format(sql, DbBName, tableB.Name));
            DataTableB = DataSetB.Tables[0];

            IsAnalyzedRows = false;
            IsDifferentStructure = false;

            var columnsInAorB = new List<string>();

            //collect column and primary keys info
            var pkColumnsA = new List<DataColumn>();
            foreach (var clA in tableA.Columns)
            {
                var col = clA as Column;
                if (col.InPrimaryKey)
                {
                    pkColumnsA.Add(DataTableA.Columns[col.Name]);
                    PrimaryKeyColumnsA.Add(col.Name);
                }
                ColumnsA.Add(col.Name);
                if (!columnsInAorB.Contains(col.Name))
                    columnsInAorB.Add(col.Name);
            }
            DataTableA.PrimaryKey = pkColumnsA.ToArray();

            var pkColumnsB = new List<DataColumn>();
            foreach (var clB in tableB.Columns)
            {
                var col = clB as Column;
                if (col.InPrimaryKey)
                {
                    pkColumnsB.Add(DataTableB.Columns[col.Name]);
                    PrimaryKeyColumnsB.Add(col.Name);
                }
                ColumnsB.Add(col.Name);
                if (!columnsInAorB.Contains(col.Name))
                    columnsInAorB.Add(col.Name);
            }
            DataTableB.PrimaryKey = pkColumnsB.ToArray();

            //compare primary keys if the same in both databases
            if (pkColumnsA.Count == 0 && pkColumnsB.Count == 0)
            {
                IsOK = false;
                IsDifferentData = true;
                ErrorText = string.Format("{2}: Table in {0} and in {1} has no primary keys.", DbAName, DbBName, TableName);
            }
            else if (pkColumnsA.Count == 0)
            {
                IsOK = false;
                IsDifferentData = true;
                ErrorText = string.Format("{1}: Table in {0} has no primary keys.", DbAName, TableName);
            }
            else if (pkColumnsB.Count == 0)
            {
                IsOK = false;
                IsDifferentData = true;
                ErrorText = string.Format("{1}: Table in {0} has no primary keys.", DbBName, TableName);
            }
            else if (pkColumnsA.Count != pkColumnsB.Count)
            {
                IsOK = false;
                IsDifferentData = true;
                ErrorText = TableName+": Different amount of primary key columns in both databases.";
            }
            else
            {
                var differentPk = false;
                for (var i = 0; i < pkColumnsA.Count; i++)
                {
                    if (pkColumnsA[i].ColumnName != pkColumnsB[i].ColumnName)
                    {
                        differentPk = true;
                        break;
                    }
                }
                if (differentPk)
                {
                    IsOK = false;
                    IsDifferentData = true;
                    ErrorText = TableName+": Primary key columns in both databases are different.";
                }
            }

            if (IsOK)
            {
                //summary of column information
                ColumnsOnlyInA = new ObservableCollection<string>();
                ColumnsOnlyInB = new ObservableCollection<string>();
                ColumnsInBothTables = new ObservableCollection<string>();

                foreach (var cn in columnsInAorB)
                {
                    if (!ColumnsA.Contains(cn) && ColumnsB.Contains(cn))
                    {
                        ColumnsOnlyInB.Add(cn);
                        continue;
                    }
                    if (!ColumnsB.Contains(cn) && ColumnsA.Contains(cn))
                    {
                        ColumnsOnlyInA.Add(cn);
                        continue;
                    }
                    if (ColumnsA.Contains(cn) && ColumnsB.Contains(cn))
                    {
                        ColumnsInBothTables.Add(cn);
                    }
                }

                var pkA = DataTableA.PrimaryKey;
                var pkB = DataTableB.PrimaryKey;
                IsDifferentData = false;

                //compare the rows of the two tables
                
                if (ColumnsOnlyInA.Count > 0 || ColumnsOnlyInB.Count > 0)
                {//different number of columns
                    IsDifferentData = true;
                    IsDifferentStructure = true;
                }
                else if (DataTableA.Rows.Count != DataTableB.Rows.Count)
                {//different number of rows
                    IsDifferentData = true;
                }
                else if (pkA.Length != pkB.Length)
                {//different number of primary key columns
                    IsDifferentData = true;
                }
                else
                {//compare the row data
                    foreach (var rwA in DataTableA.Rows)
                    {//foreach row in dtA
                        var rowA = rwA as DataRow;
                        var pkValues = new List<object>();
                        foreach (var pk in pkA)
                        {
                            pkValues.Add(rowA[pk.ColumnName]);
                        }
                        //get the row in dtB
                        var rowB = DataTableB.Rows.Find(pkValues.ToArray());
                        if (rowB == null)
                        {//no row in dtB for this row
                            IsDifferentData = true;
                            break;
                        }
                        else
                        {
                            //compare the field values
                            var isDataDifferentInRow = false;
                            foreach (var cn in ColumnsInBothTables)
                            {
                                if (cn != "dbVersion" && cn != "dbTimestamp" && cn != "dbUser")
                                {
                                    if (rowA[cn]?.ToString() != rowB[cn]?.ToString())
                                    {
                                        isDataDifferentInRow = true;
                                        break;
                                    }
                                }
                            }
                            if (isDataDifferentInRow)
                            {
                                IsDifferentData = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void analyzeTable()
        {
            Rows = new ObservableCollection<a7ComparisonRow>();

            var alreadyComparedRowsFromB = new List<DataRow>();
            var pkA = DataTableA.PrimaryKey;
            var pkB = DataTableB.PrimaryKey;
            //compare the row data
            foreach (var rwA in DataTableA.Rows)
            {//foreach row in dtA
                var rowA = rwA as DataRow;
                var pkValues = new List<object>();
                foreach (var pk in pkA)
                {
                    pkValues.Add(rowA[pk.ColumnName]);
                }
                //get the row in dtB
                var rowB = DataTableB.Rows.Find(pkValues.ToArray());
                var cr = new a7ComparisonRow(rowA, rowB, this, this._comparer);
                if (cr.IsDifferent)
                {
                    Rows.Add(cr);
                }
                if (rowB != null)
                    alreadyComparedRowsFromB.Add(rowB);
            }

            Columns = new ObservableCollection<DataGridColumn>();

            Columns.Add(new a7ComparisonRowHeaderColumn() { Width = 8 });
            Columns.Add(new a7CompMergeButtonsColumn() { Width = 60 });

            foreach (var inBoth in ColumnsInBothTables)
                Columns.Add(new a7ComparisonFieldColumn() { Binding = new Binding("[" + inBoth + "]"), Header = inBoth, ColumnName = inBoth });

            foreach (var inAOnly in ColumnsOnlyInA)
                Columns.Add(new a7ComparisonFieldColumn() { Binding = new Binding("[" + inAOnly + "]"), Header = inAOnly, ColumnName = inAOnly });

            foreach (var inBOnly in ColumnsOnlyInB)
                Columns.Add(new a7ComparisonFieldColumn() { Binding = new Binding("[" + inBOnly + "]"), Header = inBOnly, ColumnName = inBOnly });

            foreach (var rwB in DataTableB.Rows)
            {
                var rowB = rwB as DataRow;
                if (!alreadyComparedRowsFromB.Contains(rowB))
                {
                    var cr = new a7ComparisonRow(null, rowB, this, this._comparer);
                    Rows.Add(cr);
                }
            }

            IsAnalyzedRows = true;
        }

        public void SetMergeDirection(a7DbComparerDirection direction, bool isFromDbComparer = false)
        {
            if (this.Rows == null)
                analyzeTable();

            if(!isFromDbComparer)
                _comparer.SetMergeDirection(a7DbComparerDirection.Partial);
            MergeDirection = direction;
            if (direction != a7DbComparerDirection.Partial)
            {
                foreach (var row in Rows)
                {
                    if (direction == a7DbComparerDirection.None)
                    {
                        row.SetMergeDirection(direction,true);
                    }
                    else if (direction == a7DbComparerDirection.AtoB)
                    {
                        if (row.IsOnlyInB && !MergeWithDelete)
                        {
                            row.SetMergeDirection(a7DbComparerDirection.None, true);
                        }
                        else
                            row.SetMergeDirection(direction, true);
                    }
                    else if (direction == a7DbComparerDirection.BtoA)
                    {
                        if (row.IsOnlyInA && !MergeWithDelete)
                            row.SetMergeDirection(a7DbComparerDirection.None, true);
                        else
                            row.SetMergeDirection(direction, true);
                    }
                }
            }
            OnPropChanged("MergeDirection");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropChanged(string prop)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
    }
}
