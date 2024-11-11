using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using CustomerMonitoringApp.Domain.Entities;

namespace CustomerMonitoringApp.Infrastructure.Services
{
    public class ExcelReaderService
    {
        public IEnumerable<User> ParseExcelFile(string filePath)
        {
            var users = new List<User>();
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheet(1);

            foreach (var row in worksheet.RowsUsed().Skip(1)) // Skip header row
            {
                var user = new User
                {
                    UserNameProfile = row.Cell(1).GetString(),
                    UserNumberFile = row.Cell(2).GetString(),
                    UserNameFile = row.Cell(3).GetString(),
                    UserFamilyFile = row.Cell(4).GetString(),
                    UserFatherNameFile = row.Cell(5).GetString(),
                    UserBirthDayFile = row.Cell(6).GetString(),
                    UserAddressFile = row.Cell(7).GetString(),
                    UserDescriptionFile = row.Cell(8).GetString(),
                    UserSourceFile = row.Cell(9).GetString(),
                    UserTelegramID = int.Parse(row.Cell(10).GetString()) // Parse as int
                };
                users.Add(user);
            }

            return users;
        }
    }
}