Here's the updated README that includes details about the Telegram Bot features for mixing, receiving, exporting Excel files, and special configurations:

---

# Customer Monitoring Application

## Description

The **Customer Monitoring Application** is a robust WPF-based application designed to manage and monitor users and their permissions. This application enables businesses to maintain user data, handle permissions efficiently, and interact with a SQL Server database using Entity Framework Core. With a focus on user experience, it provides a user-friendly interface for managing customer information and permissions, making it suitable for small to medium-sized businesses.

In addition to user management, this application integrates a **Telegram Bot** that enhances functionality by enabling users to interact with the application through Telegram, making it easier to manage tasks on the go.

### Features

- **User Management**: Create, read, update, and delete user profiles with ease.
- **Permission Management**: Assign, manage, and revoke permissions for users, ensuring appropriate access levels.
- **SQL Server Integration**: Leverages Entity Framework Core for seamless data access and manipulation.
- **WPF User Interface**: Offers a clean, modern interface designed for easy navigation and interaction.
- **Telegram Bot Integration**:
  - **Receive Excel Files**: Users can receive Excel files directly through the Telegram bot.
  - **Export Data**: Data can be exported to Excel with customizable configurations, allowing for tailored output formats.
  - **Special Configurations**: Users can specify particular settings for data export, such as file format, specific columns, and data filtering.

## Technologies Used

- **C# (.NET 6)**: The primary programming language and framework for developing the application.
- **WPF (Windows Presentation Foundation)**: Used to build the application's user interface.
- **Entity Framework Core**: An ORM for accessing and managing data in the SQL Server database.
- **SQL Server**: The database management system used for data storage.
- **Dependency Injection**: Utilizes `Microsoft.Extensions.DependencyInjection` for managing application services.
- **Telegram Bot API**: For implementing the Telegram bot features.

## Getting Started

### Prerequisites

Before you begin, ensure you have the following installed:

- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) (local or remote)
- [Visual Studio 2022 or later](https://visualstudio.microsoft.com/vs/) (with the .NET desktop development workload)
- A Telegram bot token (you can create a bot using the BotFather on Telegram).

### Installation

1. **Clone the repository**:

   ```bash
   git clone https://github.com/YourUsername/CustomerMonitoringApp.git
   ```

2. **Navigate to the project directory**:

   ```bash
   cd CustomerMonitoringApp
   ```

3. **Open the solution file in Visual Studio**:

   Open `CustomerMonitoringApp.sln` in Visual Studio.

4. **Restore NuGet packages**:

   In Visual Studio, right-click on the solution in the Solution Explorer and select **Restore NuGet Packages**.

5. **Set up the SQL Server database**:

   - Ensure that SQL Server is running.
   - Create a new database for the application.
   - Update the connection string in `appsettings.json` to point to your SQL Server database.
   
   Example connection string:

   ```json
   "ConnectionStrings": {
       "DefaultConnection": "Server=.;Database=YourDatabaseName;Integrated Security=True;Trust Server Certificate=True"
   }
   ```

6. **Configure Telegram Bot**:

   - Add your Telegram bot token to `appsettings.json`:

   ```json
   "TelegramBot": {
       "Token": "YOUR_TELEGRAM_BOT_TOKEN"
   }
   ```

7. **Apply migrations to create the database schema**:

   Open the **Package Manager Console** in Visual Studio and run the following command:

   ```bash
   Update-Database
   ```

### Running the Application

1. Set the **Startup Project** to `CustomerMonitoringApp` in Visual Studio.
2. Press `F5` or click on the **Start** button to build and run the application.
3. The main window will open, allowing you to manage users and permissions.

## Usage

- **Managing Users**: Use the provided UI to create new users, view existing users, and update or delete users as needed.
- **Managing Permissions**: Assign and modify user permissions through the dedicated section in the application.
- **Telegram Bot Features**:
  - Send and receive Excel files through the bot.
  - Export data with custom configurations using commands in Telegram.
  - Utilize special export settings to format data as required.

## Contributing

Contributions are welcome! If you have suggestions or improvements, please fork the repository and submit a pull request.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Special thanks to the contributors and the community for their support and feedback.

---

Feel free to adjust any sections further based on your project’s specifics or any additional features you want to highlight!
