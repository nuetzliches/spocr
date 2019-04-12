using System;
using SpocR.DataContext;

namespace SpocR.Managers
{
    public class ManagerBase
    {
        private readonly IServiceProvider _serviceProvider;

        private DbContext _dbContext;
        private Generator _engine;

        public ManagerBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public DbContext DbContext => _dbContext ?? (_dbContext = (DbContext)_serviceProvider.GetService(typeof(DbContext)));
        public Generator Engine => _engine ?? (_engine = (Generator)_serviceProvider.GetService(typeof(Generator)));
    }
}