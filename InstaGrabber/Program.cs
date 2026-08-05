using InstaGrabber.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDataProtection();
builder.Services.AddSingleton<MediaLinkProtector>();

builder.Services.AddHttpClient<MediaDownloadService>(client =>
    {
        client.Timeout = TimeSpan.FromMinutes(2);
        // The CDN serves plain media to a plain client; no cookies or credentials are sent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("InstaGrabber/1.0 (+local)");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // MediaDownloadService follows redirects itself so it can re-check the host on
        // every hop. Letting the handler do it would skip that check.
        AllowAutoRedirect = false,
        UseCookies = false,
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
