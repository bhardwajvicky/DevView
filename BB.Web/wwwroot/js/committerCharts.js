// Use window scope to avoid variable redeclaration issues
window.committerCharts = {};
window.topCommittersDotNetRef = null;

window.setTopCommittersDotNetRef = (dotNetRef) => {
    window.topCommittersDotNetRef = dotNetRef;
};

window.initializeCommitterChart = (canvasId, data, displayName, isTopCommitter) => {
    // Validate data is an array
    if (!Array.isArray(data)) {
        console.error('Data is not an array:', typeof data, data);
        return;
    }
    
    if (data.length === 0) {
        console.warn('Data array is empty for canvas:', canvasId);
        return;
    }
    
    // Function to wait for DOM element with retries
    const waitForElement = (elementId, maxRetries = 10, retryDelay = 100) => {
        return new Promise((resolve, reject) => {
            let retries = 0;
            
            const checkElement = () => {
                const element = document.getElementById(elementId);
                if (element) {
                    resolve(element);
                } else if (retries < maxRetries) {
                    retries++;
                    console.log(`Waiting for element ${elementId}, retry ${retries}/${maxRetries}`);
                    setTimeout(checkElement, retryDelay);
                } else {
                    reject(new Error(`Element ${elementId} not found after ${maxRetries} retries`));
                }
            };
            
            checkElement();
        });
    };
    
    // Wait for the canvas element to be available
    waitForElement(canvasId)
        .then(chartElement => {
            // Check if Chart.js is loaded
            if (typeof Chart === 'undefined') {
                console.error('Chart.js is not loaded yet. Retrying...');
                setTimeout(() => {
                    window.initializeCommitterChart(canvasId, data, displayName, isTopCommitter);
                }, 500);
                return;
            }

            // Destroy existing chart if it exists
            if (window.committerCharts[canvasId]) {
                window.committerCharts[canvasId].destroy();
            }

            // Format dates based on grouping
            const grouping = window.selectedGrouping || 'Day';
            const dates = data.map(d => {
                const date = new Date(d.date);
                switch (grouping) {
                    case 'Month':
                        return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short' });
                    case 'Week':
                        return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
                    case 'Day':
                    default:
                        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
                }
            });

            // Prepare datasets
            const datasets = [
                {
                    label: 'Code Added',
                    data: data.map(d => d.codeLinesAdded),
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    borderColor: 'rgba(75, 192, 192, 1)',
                    borderWidth: 1,
                    fill: true
                },
                {
                    label: 'Code Removed',
                    data: data.map(d => -d.codeLinesRemoved),
                    backgroundColor: 'rgba(255, 99, 132, 0.2)',
                    borderColor: 'rgba(255, 99, 132, 1)',
                    borderWidth: 1,
                    fill: true
                }
            ];

            // Add data lines if enabled
            if (window.includeData) {
                datasets.push(
                    {
                        label: 'Data Added',
                        data: data.map(d => d.dataLinesAdded),
                        backgroundColor: 'rgba(54, 162, 235, 0.2)',
                        borderColor: 'rgba(54, 162, 235, 1)',
                        borderWidth: 1,
                        fill: true
                    },
                    {
                        label: 'Data Removed',
                        data: data.map(d => -d.dataLinesRemoved),
                        backgroundColor: 'rgba(255, 159, 64, 0.2)',
                        borderColor: 'rgba(255, 159, 64, 1)',
                        borderWidth: 1,
                        fill: true
                    }
                );
            }

            // Add config lines if enabled
            if (window.includeConfig) {
                datasets.push(
                    {
                        label: 'Config Added',
                        data: data.map(d => d.configLinesAdded),
                        backgroundColor: 'rgba(153, 102, 255, 0.2)',
                        borderColor: 'rgba(153, 102, 255, 1)',
                        borderWidth: 1,
                        fill: true
                    },
                    {
                        label: 'Config Removed',
                        data: data.map(d => -d.configLinesRemoved),
                        backgroundColor: 'rgba(255, 206, 86, 0.2)',
                        borderColor: 'rgba(255, 206, 86, 1)',
                        borderWidth: 1,
                        fill: true
                    }
                );
            }

            // Create chart
            const ctx = chartElement.getContext('2d');
            window.committerCharts[canvasId] = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: dates,
                    datasets: datasets
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: {
                        intersect: false,
                        mode: 'index'
                    },
                    plugins: {
                        title: {
                            display: false
                        },
                        legend: {
                            display: true,
                            position: 'bottom',
                            labels: {
                                usePointStyle: true,
                                pointStyle: 'circle',
                                font: { size: 12 },
                                padding: 10
                            }
                        },
                        tooltip: {
                            callbacks: {
                                title: (tooltipItems) => {
                                    return tooltipItems[0].label;
                                },
                                label: (context) => {
                                    let label = context.dataset.label || '';
                                    let value = context.raw;
                                    if (value < 0) {
                                        value = -value; // Convert back to positive for display
                                    }
                                    return `${label}: ${value}`;
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            display: true,
                            grid: {
                                display: false
                            }
                        },
                        y: {
                            display: true,
                            grid: {
                                display: true,
                                color: 'rgba(0, 0, 0, 0.1)'
                            },
                            ticks: {
                                callback: function(value) {
                                    return Math.abs(value); // Show absolute values
                                }
                            }
                        }
                    }
                }
            });
            
            console.log(`Successfully initialized chart for ${canvasId}`);
        })
        .catch(error => {
            console.error('Chart canvas element not found:', canvasId, error.message);
        });
};

window.toggleCommitterDataset = function(canvasId, datasetIndex) {
    const chart = window.committerCharts[canvasId];
    if (!chart) {
        console.warn('No chart found for', canvasId);
        return;
    }
    const dataset = chart.data.datasets[datasetIndex];
    dataset.hidden = !dataset.hidden;
    chart.update();
}; 