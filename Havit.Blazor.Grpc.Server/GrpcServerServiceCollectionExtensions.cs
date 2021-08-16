﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server;
using Havit.Blazor.Grpc.Core;
using Havit.Blazor.Grpc.Server.ServerExceptions;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;
using ProtoBuf.Meta;

namespace Havit.Blazor.Grpc.Server
{
	public static class GrpcServerServiceCollectionExtensions
	{
		public static void AddGrpcServerInfrastructure(
			this IServiceCollection services,
			Assembly assemblyToScanForDataContracts,
			Action<GrpcServiceOptions> configureOptions = null)
		{
			services.AddSingleton<ServerExceptionsGrpcServerInterceptor>();
			services.AddSingleton(BinderConfiguration.Create(marshallerFactories: new[] { ProtoBufMarshallerFactory.Create(RuntimeTypeModel.Default.RegisterApplicationContracts(assemblyToScanForDataContracts)) }, binder: new ServiceBinderWithServiceResolutionFromServiceCollection(services)));

			services.AddCodeFirstGrpc(options =>
			{
				options.Interceptors.Add<ServerExceptionsGrpcServerInterceptor>();
				options.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Optimal;

				configureOptions?.Invoke(options);
			});
		}
	}
}
