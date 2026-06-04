# Vidar - Autonomous Home Orchestration Platform

Design a modern smart home platform called "Vidar".

Vidar is not a dashboard like Home Assistant, OpenHAB, or Node-RED. Its primary purpose is not to allow users to manually control devices. Instead, it acts as an autonomous home orchestration platform that continuously optimizes comfort, energy usage, security, and automation based on high-level goals defined by the homeowner.

## Core Philosophy

The homeowner should configure goals, constraints, and preferences. Vidar should determine how to achieve those goals automatically.

Examples:

* Keep living spaces comfortable.
* Minimize electricity costs.
* Maximize self-consumption of solar energy.
* Ensure the EV is charged before departure.
* Reduce unnecessary energy consumption when nobody is home.
* Prevent overheating using shades and ventilation.
* Optimize battery, wallbox, heating, cooling, and solar production together.

The user should rarely need to interact with individual devices.

Instead of:

"Turn on Light A"

The system should think:

"Someone is present, it is dark, and this room is in use. Lighting should be enabled."

## Architecture

### Integration Layer

The platform must support integrations for:

* MQTT
* Zigbee
* Matter
* KNX
* Modbus
* Shelly
* Solar inverters
* Wallboxes
* EVs
* Weather services
* Audio systems
* Smart thermostats
* Presence sensors
* Covers and shades
* Security systems

All integrations expose standardized capabilities and hide vendor-specific details.

Devices are implementation details.

### Digital Twin / State Layer

The system maintains a continuously updated model of the home.

Examples:

* Occupancy
* Room temperatures
* Weather forecast
* Solar production
* Battery state
* EV state
* Energy prices
* Window positions
* Shade positions
* Motion activity
* User presence

Policies and decisions operate on state, not on devices.

### Decision Engine

The heart of the system.

The decision engine evaluates goals, constraints, and current state.

Example:

Goal:

* Minimize energy cost

Constraints:

* Living room temperature >= 21°C
* EV must be charged by 07:00
* Battery reserve >= 20%

The engine decides:

* When to charge the EV
* When to use battery power
* When to import from grid
* When to open or close shades
* When to start heating or cooling
* When to pre-condition rooms

The platform should explain every decision.

Example:

"Charging postponed because high solar production is expected between 10:00 and 15:00."

### Event-Driven System

The entire platform is event-driven.

Examples:

* SolarProductionChanged
* EnergyPriceChanged
* OccupancyChanged
* WeatherForecastUpdated
* EVConnected
* MotionDetected

Events trigger state updates and policy evaluations.

### User Interface

The UI is configuration-focused rather than control-focused.

Primary views:

* Goals
* Policies
* Constraints
* Energy optimization
* Device health
* Decision history
* Forecasts
* Explanations

The homepage should answer:

* What is the house doing?
* Why is it doing it?
* Is everything operating as expected?

The homepage should not primarily show hundreds of switches and buttons.

### Explainability

Every automated action must be explainable.

Examples:

* "Shade closed to reduce room temperature."
* "Heating delayed because occupancy prediction indicates nobody is home."
* "EV charging scheduled for solar surplus."

### AI and Prediction

AI should assist but never directly control devices.

Possible AI capabilities:

* Occupancy prediction
* Energy demand forecasting
* Solar forecasting
* EV charging optimization
* Comfort optimization
* Anomaly detection

AI provides recommendations and forecasts.

The policy engine remains the final decision-maker.

## Technology Stack

Preferred stack:

Backend:

* .NET
* C#
* ASP.NET Core

Database:

* MongoDB

Messaging:

* MQTT
* RestAPIs
* Modbus
* RSCP
* eISCP
* and many more vendor specific stuff

Frontend:

* React
* TypeScript

Architecture goals:

* Modular
* Event-driven
* Offline-capable
* Self-hosted
* Extensible through plugins
* Vendor-independent

The result should feel less like a smart home dashboard and more like a residential operating system that autonomously manages the home according to the homeowner's goals.
