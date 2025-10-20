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

    private bool AnyInstanceRegistered => managerFacade.InstanceHandler._instances.Count() > 0;
    private bool AnyInstancePoweredOff => managerFacade.InstanceHandler._instances.Any(i => i.OperatingState == EOperatingState.Off);
    private bool AnyInstancePoweredOn => managerFacade.InstanceHandler._instances.Any(i => i.OperatingState != EOperatingState.Off);
    private bool AnyIstanceIsRunning => managerFacade.InstanceHandler._instances.Any(i => i.OperatingState == EOperatingState.Run);

    private void ExecuteForInstances(
        Func<IInstance, bool> condition,
        Action<IInstance> action,
        string errorPrefix = "Failed")
    {
        foreach (var instance in managerFacade.InstanceHandler._instances.Where(condition))
        {
            try
            {
                action(instance);
            }
            catch (Exception e)
            {
                Snackbar.Add($"{errorPrefix} {instance.Name}: {e.Message}", Severity.Error);
            }
        }
    }

    private void PowerOnAllPLCs() => ExecuteForInstances(
            i => true, // run for all
            i => i.PowerOn(),
            "Failed to power on"
        );

    private void PowerOffAllPLCs() => ExecuteForInstances(
            i => true, // run for all
            i => i.PowerOff(),
            "Failed to power off"
        );


    private void RunAllPLCs() => ExecuteForInstances(
            i => i.OperatingState != EOperatingState.Off && i.OperatingState == EOperatingState.Stop,
            i => i.Run(),
            "Failed to run"
        );

    private void StopAllPLCs() => ExecuteForInstances(
            i => i.OperatingState == EOperatingState.Run,
            i => i.Stop(),
            "Failed to stop"
        );

    private async Task ShowConfirmation(string message, Func<Task> onConfirmed)
    {
        var parameters = new DialogParameters
        {
            ["Message"] = message
        };
        var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small };

        var dialog = DialogService.Show<ConfirmActionDialog>("Confirm Action", parameters, options);
        var result = await dialog.Result;
        if (!result.Cancelled)
        {
            await onConfirmed();
        }
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