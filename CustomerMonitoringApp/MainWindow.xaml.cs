using System.Collections.Generic;
using System.Windows;
using CustomerMonitoringApp.Application.Services;
using CustomerMonitoringApp.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerMonitoringApp.WPFApp
{
    public partial class MainWindow : Window
    {
        private readonly IServiceProvider _serviceProvider;

        public MainWindow(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
            LoadUsers();
        }

        private async void LoadUsers()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var userService = scope.ServiceProvider.GetRequiredService<UserService>();
                var users = await userService.GetAllUsersAsync();
                UserListView.ItemsSource = users; // Bind the data to ListView
            }
        }
    }
}