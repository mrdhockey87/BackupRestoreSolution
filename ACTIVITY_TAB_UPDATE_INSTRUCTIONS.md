# Instructions to Update Activity Tab in MainWindow.xaml

## Replace the Activity Tab Section

Find this section in `BackupUI/MainWindow.xaml` (around line 151-265):

```xaml
<!-- Activity/Logs Tab -->
<TabItem Name="tabActivity" Header="Activity">
```

Replace the ENTIRE `<TabItem>` section (from `<TabItem Name="tabActivity"...` to the matching `</TabItem>`) with:

```xaml
<!-- Activity/Logs Tab -->
<TabItem Name="tabActivity" Header="Activity">
	<Grid>
		<Grid.RowDefinitions>
			<RowDefinition Height="Auto"/>
			<RowDefinition Height="*"/>
			<RowDefinition Height="Auto"/>
		</Grid.RowDefinitions>
		
		<!-- Header -->
		<StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,0,0,10" Background="#F0F0F0" Height="50">
			<TextBlock Text="Backup Job Activity Logs" 
					   FontSize="18" 
					   FontWeight="Bold" 
					   VerticalAlignment="Center" 
					   Margin="15,0,20,0"/>
			<Button Content="Refresh" 
					Click="RefreshJobLogs_Click" 
					Width="100" 
					Height="32" 
					Margin="5"/>
			<Button Content="View All Activities" 
					Click="ViewAllActivitiesFromTab_Click" 
					Width="140" 
					Height="32" 
					Margin="5"/>
		</StackPanel>

		<!-- Job Logs List -->
		<DataGrid Grid.Row="1" 
				  Name="dgJobLogs" 
				  Margin="10"
				  AutoGenerateColumns="False" 
				  IsReadOnly="True"
				  SelectionMode="Single"
				  GridLinesVisibility="Horizontal"
				  AlternatingRowBackground="#F9F9F9"
				  CanUserResizeColumns="True"
				  MouseDoubleClick="JobLog_DoubleClickFromTab">
			<DataGrid.Columns>
				<DataGridTextColumn Header="Job Name" Binding="{Binding JobName}" Width="200"/>
				<DataGridTextColumn Header="Total Activities" Binding="{Binding TotalActivities}" Width="120"/>
				<DataGridTextColumn Header="Last Activity" Binding="{Binding LastActivity, StringFormat='{}{0:MM/dd/yyyy HH:mm:ss}'}" Width="150"/>
				<DataGridTextColumn Header="Success Count" Binding="{Binding SuccessCount}" Width="110">
					<DataGridTextColumn.ElementStyle>
						<Style TargetType="TextBlock">
							<Setter Property="Foreground" Value="Green"/>
							<Setter Property="FontWeight" Value="SemiBold"/>
						</Style>
					</DataGridTextColumn.ElementStyle>
				</DataGridTextColumn>
				<DataGridTextColumn Header="Warning Count" Binding="{Binding WarningCount}" Width="110">
					<DataGridTextColumn.ElementStyle>
						<Style TargetType="TextBlock">
							<Setter Property="Foreground" Value="Orange"/>
							<Setter Property="FontWeight" Value="SemiBold"/>
						</Style>
					</DataGridTextColumn.ElementStyle>
				</DataGridTextColumn>
				<DataGridTextColumn Header="Error Count" Binding="{Binding ErrorCount}" Width="100">
					<DataGridTextColumn.ElementStyle>
						<Style TargetType="TextBlock">
							<Setter Property="Foreground" Value="Red"/>
							<Setter Property="FontWeight" Value="SemiBold"/>
						</Style>
					</DataGridTextColumn.ElementStyle>
				</DataGridTextColumn>
				<DataGridTemplateColumn Header="Actions" Width="*">
					<DataGridTemplateColumn.CellTemplate>
						<DataTemplate>
							<StackPanel Orientation="Horizontal">
								<Button Content="View Details" 
										Width="100" 
										Height="25" 
										Margin="5,0"
										Tag="{Binding JobName}"
										Click="ViewJobDetailsFromTab_Click"/>
								<Button Content="Export" 
										Width="80" 
										Height="25" 
										Margin="5,0"
										Tag="{Binding JobName}"
										Click="ExportJobLogFromTab_Click"/>
							</StackPanel>
						</DataTemplate>
					</DataGridTemplateColumn.CellTemplate>
				</DataGridTemplateColumn>
			</DataGrid.Columns>
		</DataGrid>

		<!-- Footer Info -->
		<Border Grid.Row="2" 
				Background="#F0F0F0" 
				Padding="10" 
				BorderBrush="#CCC" 
				BorderThickness="1">
			<TextBlock Name="txtJobLogsStatus" 
					   Text="Double-click a job to view detailed activities" 
					   FontSize="12" 
					   Foreground="#666"/>
		</Border>
	</Grid>
</TabItem>
```

## Then Update MainWindow.xaml.cs

Add the new event handlers and the JobLogSummary class after the existing Activity tab methods.
