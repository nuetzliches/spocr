using System;
using SpocR.Internal.Common;
using SpocR.Internal.DataContext;
using SpocR.Internal.Models;

namespace SpocR.Internal.Managers
{
    internal class ManagerBase
    {
        private readonly IServiceProvider _serviceProvider;

        private DbContext _dbContext;
        private Engine _engine;

        internal ManagerBase(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        internal DbContext DbContext => _dbContext ?? (_dbContext = (DbContext)_serviceProvider.GetService(typeof(DbContext)));
        internal Engine Engine => _engine ?? (_engine = (Engine)_serviceProvider.GetService(typeof(Engine)));
    }
}