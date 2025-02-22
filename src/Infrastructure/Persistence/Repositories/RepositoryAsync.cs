using Dapper;
using DN.WebApi.Application.Abstractions.Repositories;
using DN.WebApi.Application.Abstractions.Services.General;
using DN.WebApi.Application.Constants;
using DN.WebApi.Application.Exceptions;
using DN.WebApi.Application.Wrapper;
using DN.WebApi.Domain.Contracts;
using DN.WebApi.Infrastructure.Extensions;
using DN.WebApi.Infrastructure.Persistence.Converters;
using DN.WebApi.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq.Dynamic.Core;
using DN.WebApi.Shared.DTOs.Filters;
using Mapster;
using DN.WebApi.Application.Specifications;

namespace DN.WebApi.Infrastructure.Persistence.Repositories
{
    public class RepositoryAsync : IRepositoryAsync
    {
        private readonly IStringLocalizer<RepositoryAsync> _localizer;

        // private readonly IMapper _mapper;
        private readonly ICacheService _cache;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<RepositoryAsync> _logger;
        private ISerializerService _serializer;

        public RepositoryAsync(ApplicationDbContext dbContext, ISerializerService serializer, ICacheService cache, ILogger<RepositoryAsync> logger, /*IMapper mapper, */IStringLocalizer<RepositoryAsync> localizer)
        {
            _dbContext = dbContext;
            _serializer = serializer;
            _cache = cache;
            _logger = logger;

            // _mapper = mapper;
            _localizer = localizer;
        }

        #region  Entity Framework Core : Get All
        public async Task<List<T>> GetListAsync<T>(Expression<Func<T, bool>> expression, bool noTracking = false, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            IQueryable<T> query = _dbContext.Set<T>();
            if (noTracking) query = query.AsNoTracking();
            if (expression != null) query = query.Where(expression);
            return await query.ToListAsync(cancellationToken);
        }
        #endregion
        public async Task<T> GetByIdAsync<T>(Guid entityId, BaseSpecification<T> specification, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            IQueryable<T> query = _dbContext.Set<T>();
            query = query.Specify(specification);
            return await query.FirstOrDefaultAsync();
        }

        public async Task<TDto> GetByIdAsync<T, TDto>(Guid entityId, BaseSpecification<T> specification, CancellationToken cancellationToken = default)
        where T : BaseEntity
        where TDto : IDto
        {
            var cacheKey = CacheKeys.GetCacheKey<T>(entityId);
            byte[] cachedData = !string.IsNullOrWhiteSpace(cacheKey) ? await _cache.GetAsync(cacheKey, cancellationToken) : null;
            if (cachedData != null)
            {
                await _cache.RefreshAsync(cacheKey);
                var entity = _serializer.Deserialize<TDto>(Encoding.Default.GetString(cachedData));
                return entity;
            }
            else
            {
                IQueryable<T> query = _dbContext.Set<T>();
                if (specification != null)
                    query = query.Specify(specification).Where(a => a.Id == entityId);
                var entity = await query.FirstOrDefaultAsync();
                var dto = entity.Adapt<TDto>();
                if (dto != null)
                {
                    var options = new DistributedCacheEntryOptions();
                    byte[] serializedData = Encoding.Default.GetBytes(_serializer.Serialize(dto));
                    await _cache.SetAsync(cacheKey, serializedData, options, cancellationToken);
                    return dto;
                }

                throw new EntityNotFoundException(string.Format(_localizer["entity.notfound"], typeof(T).Name, entityId));
            }
        }

        public async Task<PaginatedResult<TDto>> GetPaginatedListAsync<T, TDto>(int pageNumber, int pageSize, string[] orderBy = null, Search search = null, Expression<Func<T, bool>> expression = null, CancellationToken cancellationToken = default)
        where T : BaseEntity
        where TDto : IDto
        {
            IQueryable<T> query = _dbContext.Set<T>();
            if (expression != null) query = query.Where(expression);
            if (search != null && search.Fields.Count > 0 && !string.IsNullOrEmpty(search.Keyword))
                query = query.Search(search);
            string ordering = new OrderByConverter().ConvertBack(orderBy);
            query = !string.IsNullOrWhiteSpace(ordering) ? query.OrderBy(ordering) : query.OrderBy(a => a.Id);
            return await query.ToMappedPaginatedResultAsync<T, TDto>(pageNumber, pageSize);
        }

        public async Task<object> CreateAsync<T>(T entity)
        where T : BaseEntity
        {
            await _dbContext.Set<T>().AddAsync(entity);
            return entity.Id;
        }

        public async Task<bool> ExistsAsync<T>(Expression<Func<T, bool>> expression, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            IQueryable<T> query = _dbContext.Set<T>();
            if (expression != null) return await query.AnyAsync(expression, cancellationToken);
            return await query.AnyAsync(cancellationToken);
        }

        public Task RemoveAsync<T>(T entity)
        where T : BaseEntity
        {
            var cacheKey = CacheKeys.GetCacheKey<T>(entity.Id);
            _dbContext.Set<T>().Remove(entity);
            _cache.Remove(cacheKey);
            return Task.CompletedTask;
        }

        public async Task RemoveByIdAsync<T>(object entityId)
        where T : BaseEntity
        {
            var cacheKey = CacheKeys.GetCacheKey<T>(entityId);
            var entity = await _dbContext.Set<T>().FindAsync(entityId);
            if (entity == null) throw new EntityNotFoundException(string.Format(_localizer["entity.notfound"], typeof(T).Name, entityId));
            _dbContext.Set<T>().Remove(entity);
            _cache.Remove(cacheKey);
        }

        public Task UpdateAsync<T>(T entity)
        where T : BaseEntity
        {
            T exist = _dbContext.Set<T>().Find(entity.Id);
            _dbContext.Entry(exist).CurrentValues.SetValues(entity);
            return Task.CompletedTask;
        }

        #region Dapper
        public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object param = null, IDbTransaction transaction = null, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            return (await _dbContext.Connection.QueryAsync<T>(sql, param, transaction)).AsList();
        }

        public async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, IDbTransaction transaction = null, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(T)))
            {
                sql = sql.Replace("@tenantKey", _dbContext.TenantKey);
            }

            var entity = await _dbContext.Connection.QueryFirstOrDefaultAsync<T>(sql, param, transaction);
            if (entity == null) throw new EntityNotFoundException(string.Empty);
            return entity;
        }

        public async Task<T> QuerySingleAsync<T>(string sql, object param = null, IDbTransaction transaction = null, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            if (typeof(IMustHaveTenant).IsAssignableFrom(typeof(T)))
            {
                sql = sql.Replace("@tenantKey", _dbContext.TenantKey);
            }

            return await _dbContext.Connection.QuerySingleAsync<T>(sql, param, transaction);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<int> GetCountAsync<T>(Expression<Func<T, bool>> expression = null, CancellationToken cancellationToken = default)
        where T : BaseEntity
        {
            IQueryable<T> query = _dbContext.Set<T>();
            if (expression != null)
            {
                query = query.Where(expression);
            }

            return await query.CountAsync(cancellationToken);
        }
        #endregion
    }
}