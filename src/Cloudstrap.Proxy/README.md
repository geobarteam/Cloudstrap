# Cloudstrap.Proxy

A trusted-subsystem front door on [YARP](https://github.com/dotnet/yarp) in one registration call.

```csharp
builder.AddCloudstrapProxy();   // YARP routes/clusters from the "ReverseProxy" section
// ...
app.MapCloudstrapProxy();
```

Routes and clusters use YARP's own `ReverseProxy` configuration schema, unchanged. Both calls return YARP's
own builders, so every YARP extension stays reachable.

> This README is a skeleton. The trusted-subsystem rules (default authorization, inbound-credential stripping,
> token modes and the anonymous-route guard) are documented as they are built.

## License

MIT. `Yarp.ReverseProxy` is MIT (.NET Foundation).
