using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Project_Advanced.Models.ViewModels
{
    public class PaginatedList<T> : List<T>
    {
        public int PageIndex { get; }
        public int TotalPages { get; }
        public int PageSize { get; }
        public int TotalCount { get; }

        public bool HasPreviousPage => PageIndex > 1;
        public bool HasNextPage => PageIndex < TotalPages;

        private PaginatedList(List<T> items, int count, int pageIndex, int pageSize)
        {
            PageIndex = pageIndex;
            PageSize = pageSize;
            TotalCount = count;
            TotalPages = (int)Math.Ceiling(count / (double)pageSize);

            AddRange(items);
        }

        public static PaginatedList<T> Create(List<T> items, int totalCount, int pageIndex, int pageSize)
        {
            if (pageIndex < 1) pageIndex = 1;
            return new PaginatedList<T>(items, totalCount, pageIndex, pageSize);
        }

        public static async Task<PaginatedList<T>> CreateAsync(IQueryable<T> source, int pageIndex, int pageSize)
        {
            if (pageIndex < 1) pageIndex = 1;
            var count = await source.CountAsync();
            var items = await source.Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            return new PaginatedList<T>(items, count, pageIndex, pageSize);
        }
    }
}
