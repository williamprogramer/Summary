using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Summary.Data.Entities;

namespace Summary.Data.Configurations
{
    internal class SettingsConfiguration : BaseEntityConfiguration<SettingsEntity>
    {
        public override void Configure(EntityTypeBuilder<SettingsEntity> builder)
        {
            base.Configure(builder);
            builder.Property(s => s.Key)
               .IsRequired();
            builder.Property(s => s.Value)
               .IsRequired();
            builder.HasIndex(s => s.Key)
               .IsUnique();
        }
    }
}