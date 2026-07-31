using Microsoft.EntityFrameworkCore;
using Summary.Data.Entities;

namespace Summary.Data
{
    internal partial class SummaryDBContext(DbContextOptions<SummaryDBContext> options) : DbContext(options)
    {
        public DbSet<SettingsEntity> Settings { get; set; } = default!;

        /// <summary>
        /// Configures the model that was discovered by convention from the entity types exposed in DbSet properties on your derived context. The resulting model may be cached and re-used for subsequent instances of your derived context.
        /// </summary>
        /// <param name="modelBuilder">The builder being used to construct the model for this context.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(SummaryDBContext).Assembly);
        }
    }
}