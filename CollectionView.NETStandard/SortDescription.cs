﻿namespace CollectionViews.NETStandard
{
    public class SortDescription
    {
        public SortDescription()
        {
            Direction = ListSortDirection.Ascending;
        }
        public SortDescription(string propertyName) : this()
        {
            this.PropertyName = propertyName;
        }
        public SortDescription(string propertyName, ListSortDirection direction) : this (propertyName)
        {
            this.Direction = direction;
        }
        /// <summary>
        /// Gets or sets a value that indicates whether to sort in ascending or descending order.
        /// </summary>
        public ListSortDirection Direction { get; set; }
        /// <summary>
        /// Gets or sets the property name being used as the sorting criteria. The default value is null.
        /// </summary>
        public string PropertyName { get; set; }
    }
}