using System;
using SpocR.Common;
using SpocR.DataContext;
using SpocR.Models;

namespace SpocR.Managers
{
    public class ManagerBase
    {
        private readonly IServiceProvider _serviceProvider;

        private DbContext _dbContext;
        private Engine _engine;

        public ManagerBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public DbContext DbContext => _dbContext ?? (_dbContext = (DbContext)_serviceProvider.GetService(typeof(DbContext)));
        public Engine Engine => _engine ?? (_engine = (Engine)_serviceProvider.GetService(typeof(Engine)));
    }
}