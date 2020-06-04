﻿using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Havit.Blazor.Components.Web.Bootstrap.Filters
{
	public interface IHxFilter
	{
		RenderFragment GetLabelTemplate();

		RenderFragment GetFilterTemplate();

		RenderedEventHandler Rendered { get; set; }

		public delegate void RenderedEventHandler(IHxFilter sender, bool firstRender);
	}
}
