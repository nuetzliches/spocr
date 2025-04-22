using System;
using SpocR.DataContext;

namespace SpocR.Managers;

public class ManagerBase(IServiceProvider serviceProvider)
{
    private DbContext _dbContext;
    private Generator _engine;

    public DbContext DbContext => _dbContext ??= (DbContext)serviceProvider.GetService(typeof(DbContext));
    public Generator Engine => _engine ??= (Generator)serviceProvider.GetService(typeof(Generator));
}