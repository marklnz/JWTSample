﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Domain
{
    public class ApiClient
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Secret { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        public ApplicationTypes ApplicationType { get; set; }

        public bool Active { get; set; }

        public int RefreshTokenLifeTime { get; set; }

        [MaxLength(100)]
        public string AllowedOrigin { get; set; }
    }

    public enum ApplicationTypes
    {
        JavaScript = 1,
        NativeConfidential = 2
    };
}
