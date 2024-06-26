﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    public class SoldierMission
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required] 
        public int SoldierId { get; set; }

        [Required]
        public int MissionInstanceId { get; set; }

        [Required]
        public int MissionPositionId { get; set; }

        public virtual Soldier Soldier { get; set; }

        public virtual MissionInstance MissionInstance { get; set; }

        public virtual MissionPositions MissionPosition { get; set; }
    }
}
