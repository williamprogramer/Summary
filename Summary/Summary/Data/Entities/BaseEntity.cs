using System;

namespace Summary.Data.Entities
{
    internal class BaseEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}