using Arch.IS4Host.Data;
using Arch.IS4Host.Models;
using IdentityServer4.EntityFramework.DbContexts;
using IdentityServer4.EntityFramework.Mappers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;

namespace Arch.IS4Host
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IHostingEnvironment Environment { get; }

        public Startup(IConfiguration configuration, IHostingEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            //Store connection string as a var
            var connectionString = Configuration.GetConnectionString("DefaultConnection");

            //Store assembly for migration
            var migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
            
            //Replace DbContext database from SqlLite in template to Postgres
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();

            services.AddMvc();

            services.Configure<IISOptions>(iis =>
            {
                iis.AuthenticationDisplayName = "Windows";
                iis.AutomaticAuthentication = false;
            });

            var builder = services.AddIdentityServer()
                //Use Postgres Database for stroing configuration data
                .AddConfigurationStore(confiqDb=>{
                    confiqDb.ConfigureDbContext=db=>db.UseNpgsql(connectionString,
                    sql=>sql.MigrationsAssembly(migrationsAssembly));
                    })

                //Use Postgres Databse for storing operational data
                .AddOperationalStore(operationalDb =>
                 {
                     operationalDb.ConfigureDbContext = db => db.UseNpgsql(connectionString,
                       sql => sql.MigrationsAssembly(migrationsAssembly));
                 })
                .AddAspNetIdentity<ApplicationUser>();

            if (Environment.IsDevelopment())
            {
                builder.AddDeveloperSigningCredential();
            }
            else
            {
                throw new Exception("need to configure key material");
            }

            services.AddAuthentication()
                .AddGoogle(options =>
                {
                    options.ClientId = "708996912208-9m4dkjb5hscn7cjrn5u0r4tbgkbj1fko.apps.googleusercontent.com";
                    options.ClientSecret = "wdfPY6t8H8cecgjlxud__4Gh";
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            InitializeDatabase(app);

            if (Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            app.UseIdentityServer();
            app.UseMvcWithDefaultRoute();
        }

        private void InitializeDatabase(IApplicationBuilder app){
            //Using a services scope
            using (var serviceScope =
                app.ApplicationServices.GetService<IServiceScopeFactory>()
                .CreateScope())
            {
                //Create Persisted Grant Database (using a single db here) 
                //if doesn't exist, and run outstanding migrations
               var PersistedGrantDbContext= serviceScope.ServiceProvider
                    .GetRequiredService<PersistedGrantDbContext>();
               PersistedGrantDbContext.Database.Migrate();

                //Create IS4 Configuration Database (using a single db here) 
                //if doesn't exist, and run outstanding migrations
                var configDbContext = serviceScope.ServiceProvider
                    .GetRequiredService<ConfigurationDbContext>();
                configDbContext.Database.Migrate();

                //Generating the records corresponding to the Clients, IdentityResources, and
                //API Resources that are defined in Config class
                if(!configDbContext.Clients.Any())
                {
                    foreach (var client in Config.GetClients())
                    {
                        configDbContext.Clients.Add(client.ToEntity());
                    }

                    configDbContext.SaveChanges();
                }

                if (!configDbContext.IdentityResources.Any())
                {
                    foreach (var res in Config.GetIdentityResources())
                    {
                        configDbContext.IdentityResources.Add(res.ToEntity());
                    }

                    configDbContext.SaveChanges();
                }

                if (!configDbContext.ApiResources.Any())
                {
                    foreach (var api in Config.GetApis())
                    {
                        configDbContext.ApiResources.Add(api.ToEntity());
                    }

                    configDbContext.SaveChanges();
                }
            }
        }
    }
}
