﻿// <auto-generated />
using System;
using CustomerMonitoringApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace CustomerMonitoringApp.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.10")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.CallHistory", b =>
                {
                    b.Property<int>("CallId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("CallId"));

                    b.Property<string>("CallDateTime")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("CallType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int?>("CallerUserId")
                        .HasColumnType("int");

                    b.Property<string>("DestinationPhoneNumber")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int>("Duration")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .HasDefaultValue(0);

                    b.Property<string>("FileName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<int?>("RecipientUserId")
                        .HasColumnType("int");

                    b.Property<string>("SourcePhoneNumber")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int?>("UserId")
                        .HasColumnType("int");

                    b.HasKey("CallId");

                    b.HasIndex("CallerUserId");

                    b.HasIndex("RecipientUserId");

                    b.HasIndex("UserId");

                    b.ToTable("CallHistories");
                });

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.User", b =>
                {
                    b.Property<int>("UserId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("UserId"));

                    b.Property<string>("UserAddressFile")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<DateTime?>("UserBirthDayFile")
                        .HasColumnType("datetime2");

                    b.Property<string>("UserDescriptionFile")
                        .IsRequired()
                        .HasMaxLength(500)
                        .HasColumnType("nvarchar(500)");

                    b.Property<string>("UserFamilyFile")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("UserFatherNameFile")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("UserNameFile")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("UserNameProfile")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<string>("UserNumberFile")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<string>("UserSourceFile")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<long>("UserTelegramID")
                        .HasColumnType("bigint");

                    b.HasKey("UserId");

                    b.HasIndex("UserTelegramID")
                        .IsUnique();

                    b.ToTable("Users");
                });

            modelBuilder.Entity("UserPermission", b =>
                {
                    b.Property<int>("PermissionId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("PermissionId"));

                    b.Property<string>("PermissionDescription")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("PermissionType")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<int>("UserId")
                        .HasColumnType("int");

                    b.Property<int>("UserTelegramID")
                        .HasColumnType("int");

                    b.HasKey("PermissionId");

                    b.HasIndex("UserTelegramID");

                    b.ToTable("UserPermissions");
                });

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.CallHistory", b =>
                {
                    b.HasOne("CustomerMonitoringApp.Domain.Entities.User", "CallerUser")
                        .WithMany()
                        .HasForeignKey("CallerUserId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("CustomerMonitoringApp.Domain.Entities.User", "RecipientUser")
                        .WithMany()
                        .HasForeignKey("RecipientUserId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("CustomerMonitoringApp.Domain.Entities.User", null)
                        .WithMany("CallHistory")
                        .HasForeignKey("UserId");

                    b.Navigation("CallerUser");

                    b.Navigation("RecipientUser");
                });

            modelBuilder.Entity("UserPermission", b =>
                {
                    b.HasOne("CustomerMonitoringApp.Domain.Entities.User", "User")
                        .WithMany("UserPermissions")
                        .HasForeignKey("UserTelegramID")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("User");
                });

            modelBuilder.Entity("CustomerMonitoringApp.Domain.Entities.User", b =>
                {
                    b.Navigation("CallHistory");

                    b.Navigation("UserPermissions");
                });
#pragma warning restore 612, 618
        }
    }
}
