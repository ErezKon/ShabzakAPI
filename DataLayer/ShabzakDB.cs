using Azure;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer
{
    public class ShabzakDB: DbContext
    {
        public ShabzakDB()
        {
            
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = @"";
                optionsBuilder.UseSqlServer(@"");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Soldier>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired();
                entity.Property(e => e.PersonalNumber)
                    .IsRequired();
                entity.Property(e => e.Phone)
                    .IsRequired();
                entity.Property(e => e.Position)
                    .IsRequired();
                entity.Property(e => e.Platoon)
                    .IsRequired();
                entity.Property(e => e.Company)
                    .IsRequired();

                entity.HasMany(e => e.Missions)
                    .WithOne(e => e.Soldier)
                    .HasForeignKey(e => e.SoldierId);
            });

            modelBuilder.Entity<Mission>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .IsRequired();
                entity.Property(e => e.SoldiersRequired)
                    .IsRequired();
                entity.Property(e => e.CommandersRequired)
                    .IsRequired();
                entity.Property(e => e.Duration)
                    .IsRequired();
                entity.Property(e => e.IsSpecial)
                    .IsRequired();

                entity.HasMany(e => e.MissionPositions)
                    .WithOne(e => e.Mission)
                    .HasForeignKey(e => e.MissionId);
            });

            modelBuilder.Entity<SoldierMission>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SoldierId)
                    .IsRequired();
                entity.Property(e => e.MissionId)
                    .IsRequired();
                entity.Property(e => e.MissionPositionId)
                    .IsRequired();
                entity.Property(e => e.Time)
                    .IsRequired();

                entity.HasOne(e => e.Mission)
                    .WithMany(e => e.SoldierMissions)
                    .HasForeignKey(e => e.MissionId);

                entity.HasOne(e => e.Soldier)
                    .WithMany(e => e.Missions)
                    .HasForeignKey(e => e.MissionId);

                entity.HasOne(e => e.MissionPosition)
                    .WithMany(e => e.Soldiers)
                    .HasForeignKey(e => e.MissionId);
            });

            modelBuilder.Entity<MissionPositions>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.MissionId)
                    .IsRequired();
                entity.Property(e => e.Position)
                    .IsRequired();
                entity.Property(e => e.Count)
                    .IsRequired();

                entity.HasOne(e => e.Mission)
                    .WithMany(e => e.MissionPositions)
                    .HasForeignKey(e => e.MissionId);

                entity.HasMany(e => e.Soldiers)
                    .WithOne(e => e.MissionPosition)
                    .HasForeignKey(e => e.MissionId);
            });

            modelBuilder.Entity<Vacation>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SoldierId)
                    .IsRequired();
                entity.Property(e => e.From)
                    .IsRequired();
                entity.Property(e => e.To)
                    .IsRequired();

                entity.HasOne(e => e.Soldier)
                    .WithMany(e => e.Vacation)
                    .HasForeignKey(e => e.SoldierId);
            });

        }
    }
}
