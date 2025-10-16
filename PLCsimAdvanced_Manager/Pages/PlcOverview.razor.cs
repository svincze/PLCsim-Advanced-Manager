using MudBlazor;
using PLCsimAdvanced_Manager.Components;
using PLCsimAdvanced_Manager.Components.Instance;
using PLCsimAdvanced_Manager.Services;
using Siemens.Simatic.Simulation.Runtime;
using System.Diagnostics;

namespace PLCsimAdvanced_Manager.Pages;

public partial class PlcOverview
{
    // private List<IInstance>? instances;
    // private IInstance? _selectedInstance;
    private MudTable<IInstance> mudTable;
    private bool IsEditingCommunicationInterface = false;
    private Dictionary<IInstance, int> instanceCpuAffinity = new();

    protected override void OnInitialized()
    {
        managerFacade.InstanceHandler.UpdateExistingInstances();
        managerFacade.InstanceHandler.OnInstanceChanged += OnInstanceChanged;
        managerFacade.InstanceHandler.OnIssue += OnIssue;
        base.OnInitialized();
    }

    private void OnInstanceChanged(object? sender, InstanceChangedEventArgs e)
    {
        InvokeAsync(StateHasChanged);
        Snackbar.Add(e.Message, Severity.Success);
    }

    private void OnIssue(object? sender, Exception e)
    {
        Snackbar.Add(e.Message, Severity.Error);
    }


    private void OpenDialogNewPLC()
    {
        DialogOptions closeOnEscapeKey = new DialogOptions() { CloseOnEscapeKey = true, FullWidth = true };

        DialogService.Show<NewPlcDialog>("Add PLC Instance", closeOnEscapeKey);
    }

    private void OpenDialogStorage()
    {
        DialogOptions closeOnEscapeKey = new DialogOptions()
        { CloseOnEscapeKey = true, FullWidth = true, CloseButton = true };

        DialogService.Show<StorageDialog>("Storage", closeOnEscapeKey);
    }

    private void OpenDialogSetIPSettings(IInstance selectedInstance)
    {
        DialogOptions closeOnEscapeKey = new DialogOptions()
        { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, CloseButton = true };
        var parameters = new DialogParameters();
        parameters.Add("selectedInstance", selectedInstance);
        DialogService.Show<SetIPSettingsDialog>($"IP Settings: {selectedInstance.Name}", parameters, closeOnEscapeKey);
    }

    private void OpenDialogPLCSettings(IInstance selectedInstance)
    {
        DialogOptions closeOnEscapeKey = new DialogOptions()
        { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
        var parameters = new DialogParameters();
        parameters.Add("selectedInstance", selectedInstance);

        DialogService.Show<PlcSettings>($"PLC Settings: {selectedInstance.Name}", parameters, closeOnEscapeKey);
    }

    private void OpenDialogNetInterfaceMapping(IInstance selectedInstance)
    {
        DialogOptions closeOnEscapeKey = new DialogOptions()
        { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true };
        var parameters = new DialogParameters();
        parameters.Add("selectedInstance", selectedInstance);

        DialogService.Show<NetInterfaceMappingSettings>($"Net Interface Mapping: {selectedInstance.Name}", parameters,
            closeOnEscapeKey);
    }

    private void OpenDialogSnapshots(IInstance selectedInstance)
    {
        DialogOptions closeOnEscapeKey = new DialogOptions()
        { CloseOnEscapeKey = true, FullWidth = true, CloseButton = true };
        var parameters = new DialogParameters();
        parameters.Add("selectedInstance", selectedInstance);
        DialogService.Show<SnapshotsDialog>("Snapshots", parameters, closeOnEscapeKey);
    }

    public void RemoveInstance(IInstance instance)
    {
        var parameters = new DialogParameters<DeleteDialog>();
        parameters.Add(x => x.Instance, instance);
        parameters.Add(x => x.Instances, managerFacade.InstanceHandler._instances);

        var options = new DialogOptions() { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall };

        DialogService.Show<DeleteDialog>("Delete Instance", parameters, options);
    }


    public void SeeSnapshots(IInstance instane)
    {
        OpenDialogSnapshots(instane);
    }

    private string RunAllButtonText => AnyPLCIsRunning ? "Stop All" : "Run All";
    private string RunAllButtonIcon => AnyPLCIsRunning ? Icons.Material.Outlined.Stop : Icons.Material.Outlined.PlayArrow;

    private bool AnyPLCIsRunning => managerFacade.InstanceHandler._instances.Any(i => i.OperatingState == EOperatingState.Run);

    private void RunOrStopAllPLCs()
    {
        if (AnyPLCIsRunning)
        {
            foreach (var instance in managerFacade.InstanceHandler._instances)
            {
                try
                {
                    if (instance.OperatingState == EOperatingState.Run)
                        instance.Stop();
                }
                catch (Exception e)
                {
                    Snackbar.Add($"Failed to stop {instance.Name}: {e.Message}", Severity.Error);
                }
            }
        }
        else
        {
            foreach (var instance in managerFacade.InstanceHandler._instances)
            {
                try
                {
                    if (instance.OperatingState == EOperatingState.Stop)
                        instance.Run();
                }
                catch (Exception e)
                {
                    Snackbar.Add($"Failed to run {instance.Name}: {e.Message}", Severity.Error);
                }
            }
        }
    }

    private async Task ShowConfirmation(string message, Func<Task> onConfirmed)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = message,
            ["OnConfirmed"] = onConfirmed
        };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small };

        var dialog = DialogService.Show<ConfirmActionDialog>("Confirm Action", parameters, options);
        await dialog.Result;
    }

    // Usage:
    private async Task ConfirmRunOrStopAllPLCs()
    {
        var message = AnyPLCIsRunning
            ? "Are you sure you want to stop all running PLCs?"
            : "Are you sure you want to run all stopped PLCs?";

        await ShowConfirmation(message, async () => RunOrStopAllPLCs());
    }

    private async Task ConfirmSetCpuAffinity()
    {
        var message = "Assign CPU affinity for all PLC instances?";

        await ShowConfirmation(message, async () => AssignCpuAffinityToAllInstances());
    }

    private string selectedRowStyleFunc(IInstance i, int rowNumber)
    {
        if (mudTable.SelectedItem != null && mudTable.SelectedItem.Equals(i))
        {
            return "background-color: lightgrey";
        }

        return string.Empty;
    }

    private void AssignCpuAffinityToAllInstances()
    {
        int cpuIndex = 1;
        int cpuCount = Environment.ProcessorCount;
        int defaultMask = 255;
        var processes = Process.GetProcessesByName("Siemens.Simatic.Simulation.Runtime.Instance.x64");
        var instances = managerFacade.InstanceHandler._instances.Where(_w => _w.OperatingState == EOperatingState.Run).ToList();

        if (processes.Count() != 0 && instances.Count != 0)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                var proc = processes[i];
                if (proc != null && instance != null)
                {
                    long affinityMask = 1L << (cpuIndex % cpuCount); // Assign round-robin
                    proc.ProcessorAffinity = processes.Count() > cpuCount ? defaultMask : (IntPtr)affinityMask;
                    instanceCpuAffinity[instance] = cpuIndex % cpuCount; // Store assigned CPU
                    cpuIndex++;
                }
            }
        }
    }
}