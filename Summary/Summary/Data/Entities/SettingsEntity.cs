namespace Summary.Data.Entities
{
    public class SettingsEntity : BaseEntity
    {
        /// <summary>
        /// Gets or sets the key of the setting. This property is used to identify the setting in the database.
        /// </summary>
        public string Key { get; set; } = string.Empty;
        /// <summary>
        /// Gets or sets the value of the setting. This property holds the actual data associated with the key in the database.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}