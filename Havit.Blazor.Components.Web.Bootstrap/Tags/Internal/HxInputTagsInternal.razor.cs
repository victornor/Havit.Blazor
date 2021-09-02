﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Havit.Blazor.Components.Web.Infrastructure;
using Havit.Diagnostics.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Havit.Blazor.Components.Web.Bootstrap.Internal
{
	//ad 1) Existující vs nové: Nastavitelné
	//ad 2) Primárně stringy, když něco jiného pro multipicker, je to bonus.Nepředpokládá se pro multipicker zakládání nových dat,
	//ad 3) Ala grid, tj. request/response.

	//4. Pořadí - prioritou je, že se při zadávání nepřeřadí, tj. nové nakonec.

	public partial class HxInputTagsInternal
	{
		[Parameter] public bool AllowCustomTags { get; set; } = true;

		[Parameter] public List<string> Value { get; set; }
		[Parameter] public EventCallback<List<string>> ValueChanged { get; set; }

		[Parameter] public InputTagsDataProviderDelegate DataProvider { get; set; }

		/// <summary>
		/// Minimal number of characters to start suggesting. Default is <c>2</c>.
		/// </summary>
		[Parameter] public int MinimumLength { get; set; }

		/// <summary>
		/// Short hint displayed in the input field before the user enters a value.
		/// </summary>
		[Parameter] public string Placeholder { get; set; }

		/// <summary>
		/// Debounce delay in miliseconds. Default is <c>300 ms</c>.
		/// </summary>
		[Parameter] public int Delay { get; set; }

		[Parameter] public string InputCssClass { get; set; }

		[Parameter] public string InputId { get; set; }

		[Parameter] public bool EnabledEffective { get; set; }

		[Parameter] public LabelType LabelTypeEffective { get; set; }

		/// <summary>
		/// Offset between dropdown input and dropdown menu
		/// </summary>
		[Parameter] public (int X, int Y) InputOffset { get; set; }

		[Inject] protected IJSRuntime JSRuntime { get; set; }

		private string dropdownId = "hx" + Guid.NewGuid().ToString("N");
		private System.Timers.Timer timer;
		private string userInput = String.Empty;
		private CancellationTokenSource cancellationTokenSource;
		private List<string> suggestions;
		private bool userInputModified;
		private bool isDropdownOpened = false;
		private bool blurInProgress;
		private bool currentlyFocused;
		private IJSObjectReference jsModule;
		private HxInputTagsAutosuggestInput autosuggestInput;
		//private TValue lastKnownValue;
		private bool dataProviderInProgress;
		private DotNetObjectReference<HxInputTagsInternal> dotnetObjectReference;

		public HxInputTagsInternal()
		{
			dotnetObjectReference = DotNetObjectReference.Create(this);
		}

		private async Task AddTagWithEventCallback(string tag)
		{
			if ((Value != null) && Value.Contains(tag))
			{
				return;
			}

			if (Value == null)
			{
				Value = new List<string> { tag };
			}
			else
			{
				Value = new List<string>(Value); // do not change the insntace, create a copy!
				Value.Add(tag);
			}
			await ValueChanged.InvokeAsync(Value);
		}

		private async Task RemoveTagWithEventCallback(string tag)
		{
			if (Value == null)
			{
				return;
			}

			Value = Value.Except(new string[] { tag }).ToList();
			await ValueChanged.InvokeAsync(Value);
		}

		protected override void OnParametersSet()
		{
			base.OnParametersSet();

			Contract.Requires<InvalidOperationException>(DataProvider != null, $"{GetType()} requires a {nameof(DataProvider)} parameter.");
		}

		public async ValueTask FocusAsync()
		{
			await autosuggestInput.FocusAsync();
		}

		private async Task HandleInputInput(string newUserInput)
		{
			// user changes an input
			userInput = newUserInput;
			userInputModified = true;

			timer?.Stop(); // if waiting for an interval, stop it
			cancellationTokenSource?.Cancel(); // if already loading data, cancel it
			dataProviderInProgress = false; // data provider is no more in progress				 

			// start new time interval
			if (userInput.Length >= MinimumLength)
			{
				if (timer == null)
				{
					timer = new System.Timers.Timer();
					timer.AutoReset = false; // just once
					timer.Elapsed += HandleTimerElapsed;
				}
				timer.Interval = Delay;
				timer.Start();
			}
			else
			{
				// or close a dropdown
				suggestions = null;
				await DestroyDropdownAsync();
			}
		}

		private async void HandleTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			// when a time interval reached, update suggestions
			await InvokeAsync(async () =>
			{
				await UpdateSuggestionsAsync();
			});
		}

		private async Task HandleInputFocus()
		{
			// when an input gets focus, close a dropdown
			currentlyFocused = true;
			await DestroyDropdownAsync();
		}

		// Kvůli updatovanání HTML a kolizi s bootstrap Dropdown nesmíme v InputBlur přerenderovat html!
		private void HandleInputBlur()
		{
			currentlyFocused = false;
			// when user clicks back button in browser this method can be called after it is disposed!
			blurInProgress = true;
			timer?.Stop(); // if waiting for an interval, stop it
			cancellationTokenSource?.Cancel(); // if waiting for an interval, stop it
			dataProviderInProgress = false; // data provider is no more in progress				 
		}

		private async Task UpdateSuggestionsAsync()
		{
			// Cancelation is performed in HandleInputInput method
			cancellationTokenSource?.Dispose();

			cancellationTokenSource = new CancellationTokenSource();
			CancellationToken cancellationToken = cancellationTokenSource.Token;

			dataProviderInProgress = true;
			StateHasChanged();

			InputTagsDataProviderRequest request = new InputTagsDataProviderRequest
			{
				UserInput = userInput,
				CancellationToken = cancellationToken
			};

			InputTagsDataProviderResult result;
			try
			{
				result = await DataProvider.Invoke(request);
			}
			catch (OperationCanceledException) // gRPC stack does not set the operationFailedException.CancellationToken, do not check in when-clause
			{
				return;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			dataProviderInProgress = false;
			suggestions = result.Data.ToList();

			if (suggestions?.Any() ?? false)
			{
				await OpenDropdownAsync();
			}
			else
			{
				await DestroyDropdownAsync();
			}

			StateHasChanged();
		}

		private async Task HandleItemClick(string tag)
		{
			// user clicked on an item in the "dropdown".
			await AddTagWithEventCallback(tag);
			userInput = String.Empty;
			userInputModified = false;
		}

		protected override async Task OnAfterRenderAsync(bool firstRender)
		{
			await base.OnAfterRenderAsync(firstRender);

			if (blurInProgress)
			{
				blurInProgress = false;
				if (userInputModified && !isDropdownOpened)
				{
					userInput = String.Empty;
					userInputModified = false;
					StateHasChanged();
				}
			}
		}

		#region OpenDropdownAsync, DestroyDropdownAsync, EnsureJsModuleAsync
		private async Task OpenDropdownAsync()
		{
			if (!isDropdownOpened)
			{
				await EnsureJsModuleAsync();
				await jsModule.InvokeVoidAsync("open", autosuggestInput.InputElement, dotnetObjectReference);
				isDropdownOpened = true;
			}
		}

		private async Task DestroyDropdownAsync()
		{
			if (isDropdownOpened)
			{
				await EnsureJsModuleAsync();

				await jsModule.InvokeVoidAsync("destroy", autosuggestInput.InputElement);
				isDropdownOpened = false;
			}
		}

		private async Task EnsureJsModuleAsync()
		{
			jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Havit.Blazor.Components.Web.Bootstrap/" + nameof(HxInputTags) + ".js");
		}

		// TODO!!!
		[JSInvokable("HxInputTagsInternal_HandleDropdownHidden")]
		public Task HandleDropdownHidden()
		{
			if (userInputModified && !currentlyFocused)
			{
				userInput = String.Empty;
				userInputModified = false;
			}

			return Task.CompletedTask;
		}
		#endregion

		protected async Task HandleRemoveClickAsync(string tag)
		{
			await RemoveTagWithEventCallback(tag);
		}


		///// <summary>
		///// Selects value from item.
		///// Not required when TValue is same as TItemTime.
		///// </summary>
		//[Parameter] public Func<TItem, TValue> ValueSelector { get; set; }

		///// <summary>
		///// Selects text to display from item.
		///// When not set ToString() is used.
		///// </summary>
		//[Parameter] public Func<TItem, string> TextSelector { get; set; }

		///// <summary>
		///// Gets item from <see cref="Value"/>.
		///// </summary>
		//[Parameter] public Func<TValue, Task<TItem>> ItemFromValueResolver { get; set; }

		public async ValueTask DisposeAsync()
		{
			timer?.Dispose();
			timer = null;
			cancellationTokenSource?.Dispose();
			cancellationTokenSource = null;

			dotnetObjectReference.Dispose();

			if (jsModule != null)
			{
				await jsModule.DisposeAsync();
			}
		}

	}
}