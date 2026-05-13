// SingleThreadDispatcher.cs

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MauiDataDemo;

/// <summary>
/// Provides a dedicated, single-threaded execution context for serialising the
/// execution of actions on a privately owned background thread.
/// </summary>
public partial class SingleThreadDispatcher : IDispatcher, IDisposable
{
	/// <summary>
	/// Gets the logger instance for diagnostic logging.
	/// </summary>
	public static ILogger? Logger => field ??= IPlatformApplication.Current?.Services.GetService<ILogger<SingleThreadDispatcher>>();

	readonly BlockingCollection<Action> queue = new();
	readonly Thread thread;
	readonly CancellationTokenSource cts = new();
	volatile bool disposed;

	/// <summary>
	/// Gets the managed identifier of the dispatcher thread.
	/// </summary>
	public int ThreadId => thread.ManagedThreadId;

	/// <summary>
	/// Initialises a new instance of the <see cref="SingleThreadDispatcher"/> class
	/// with a dedicated background thread.
	/// </summary>
	/// <param name="name">
	/// The name assigned to the dispatcher thread.
	/// </param>
	public SingleThreadDispatcher(string name)
	{
		thread = new Thread(Run)
		{
			IsBackground = true,
			Name = name
		};
		thread.Start();
	}

	void Run()
	{
		try
		{
			foreach (var action in queue.GetConsumingEnumerable(cts.Token))
			{
				try
				{
					action();
				}
				catch (OperationCanceledException) when (cts.IsCancellationRequested)
				{
					// Normal shutdown path - break out of the loop to allow the thread to exit cleanly.
					break;
				}
				catch (Exception ex)
				{
					// Log exceptions from individual actions but continue processing subsequent actions.
					Logger?.LogError(ex, "Exception encountered while handling action: {Message}", ex.Message);
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Normal shutdown during enumeration - no logging necessary.
		}
		catch (Exception ex)
		{
			// Terminal dispatcher failure – this should never happen in normal operation.
			Logger?.LogCritical(ex, "Unexpected exception in SingleThreadDispatcher: {Message}", ex.Message);
		}
	}

	/// <summary>
	/// Gets a value indicating whether the caller must dispatch execution to the dispatcher thread.
	/// </summary>
	public bool IsDispatchRequired => Thread.CurrentThread != thread;

	/// <summary>
	/// Schedules the specified action for execution on the dispatcher thread.
	/// </summary>
	/// <param name="action">The action to execute.</param>
	/// <returns><see langword="true"/> if the action was accepted for dispatch; otherwise, <see langword="false"/>.</returns>
	public bool Dispatch(Action action)
	{
		if (disposed || cts.IsCancellationRequested)
		{
			return false;
		}

		try
		{
			queue.Add(action, cts.Token);
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, ex.Message);
			return false;
		}

		return true;
	}

	/// <summary>
	/// Schedules the specified action for execution on the dispatcher thread
	/// after the given delay.
	/// </summary>
	/// <param name="delay">The amount of time to wait before executing the action.</param>
	/// <param name="action">The action to execute.</param>
	/// <returns><see langword="true"/> if the action was accepted for dispatch;otherwise, <see langword="false"/>.
	/// </returns>
	public bool DispatchDelayed(TimeSpan delay, Action action)
	{
		if (disposed || cts.IsCancellationRequested)
		{
			return false;
		}

		return Dispatch(() =>
		{
			try
			{
				Task.Delay(delay, cts.Token).Wait(cts.Token);
				action();
			}
			catch (OperationCanceledException)
			{
				// cancelled – ignore
			}
			catch (Exception ex)
			{
				Logger?.LogError(ex, ex.Message);
				throw;
			}
		});
	}

	/// <summary>
	/// Creates a dispatcher timer associated with this dispatcher.
	/// </summary>
	/// <returns>An <see cref="IDispatcherTimer"/> instance.</returns>
	/// <exception cref="NotImplementedException">Thrown because timer support has not been implemented.</exception>
	public IDispatcherTimer CreateTimer()
	{
		throw new NotImplementedException("CreateTimer not implemented");
	}

	/// <summary>
	/// Releases all resources used by the dispatcher and stops the dispatcher thread.
	/// </summary>
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Releases managed resources used by the dispatcher.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> when called from Dispose; otherwise, <see langword="false"/>.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (!disposing || disposed)
		{
			return;
		}

		disposed = true;

		// Stop accepting new work first
		queue.CompleteAdding();

		// Signal the dispatcher thread to exit
		cts.Cancel();

		// Wait for the dispatcher thread to shut down cleanly
		thread.Join();

		// Release managed resources
		queue.Dispose();
		cts.Dispose();
	}
}