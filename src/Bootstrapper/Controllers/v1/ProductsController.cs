using DN.WebApi.Application.Abstractions.Services.Catalog;
using DN.WebApi.Domain.Constants;
using DN.WebApi.Infrastructure.Identity.Permissions;
using DN.WebApi.Shared.DTOs.Catalog;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DN.WebApi.Bootstrapper.Controllers.v1
{
    public class ProductsController : BaseController
    {
        private readonly IProductService _service;

        public ProductsController(IProductService service)
        {
            _service = service;
        }

        [HttpGet("{id}")]
        [MustHavePermission(Permissions.Products.View)]
        public async Task<IActionResult> GetAsync(Guid id)
        {
            var productDetails = await _service.GetProductDetailsAsync(id);
            return Ok(productDetails);
        }

        [HttpGet]
        [MustHavePermission(Permissions.Products.ListAll)]
        public async Task<IActionResult> GetListAsync(ProductListFilter filter)
        {
            var productDetails = await _service.GetProductsAsync(filter);
            return Ok(productDetails);
        }

        [HttpGet("dapper")]
        public async Task<IActionResult> GetDapperAsync(Guid id)
        {
            var productDetails = await _service.GetByIdUsingDapperAsync(id);
            return Ok(productDetails);
        }

        [HttpPost]
        public async Task<IActionResult> CreateAsync(CreateProductRequest request)
        {
            return Ok(await _service.CreateProductAsync(request));
        }

        [HttpPut]
        public async Task<IActionResult> UpdateAsync(UpdateProductRequest request, Guid id)
        {
            return Ok(await _service.UpdateProductAsync(request, id));
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteAsync(Guid id)
        {
            var productDetails = await _service.DeleteProductAsync(id);
            return Ok(productDetails);
        }
    }
}